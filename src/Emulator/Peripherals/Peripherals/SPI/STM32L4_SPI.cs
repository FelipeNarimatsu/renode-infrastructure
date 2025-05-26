// Copyright (c) 2010-2025 Antmicro
// This file is licensed under the MIT License.
// Full license text is available in 'licenses/MIT.txt'.

using Antmicro.Renode.Peripherals.Bus;
using Antmicro.Renode.Logging;
using Antmicro.Renode.Core.Structure;
using Antmicro.Renode.Core;
using Antmicro.Renode.Core.Structure.Registers;
using Antmicro.Renode.Utilities.Collections;

namespace Antmicro.Renode.Peripherals.SPI
{
    public sealed class STM32L4_SPI : NullRegistrationPointPeripheralContainer<ISPIPeripheral>, IWordPeripheral, IDoubleWordPeripheral, IBytePeripheral, IKnownSize
    {
        public STM32L4_SPI(IMachine machine, int bufferCapacity = DefaultBufferCapacity) : base(machine)
        {
            IRQ = new GPIO();
            DMARecieve = new GPIO();
            registers = new DoubleWordRegisterCollection(this);
            receiveBuffer = new CircularBuffer<byte>(DefaultBufferCapacity);
            DefineRegisters();
            Reset();
        }

        public byte ReadByte(long offset)
        {
            // byte interface is there for DMA
            if(offset % 4 == 0)
            {
                return (byte)ReadDoubleWord(offset);
            }
            this.LogUnhandledRead(offset);
            return 0;
        }

        public void WriteByte(long offset, byte value)
        {
            if(offset % 4 == 0)
            {
                WriteDoubleWord(offset, (uint)value);
            }
            else
            {
                this.LogUnhandledWrite(offset, value);
            }
        }

        public ushort ReadWord(long offset)
        {
            return (ushort)ReadDoubleWord(offset);
        }

        public void WriteWord(long offset, ushort value)
        {
            WriteDoubleWord(offset, (uint)value);
        }

        public uint ReadDoubleWord(long offset)
        {
            return registers.Read(offset);
        }

        public void WriteDoubleWord(long offset, uint value)
        {
            registers.Write(offset, value);
        }

        public override void Reset()
        {
            IRQ.Unset();
            DMARecieve.Unset();
            lock(receiveBuffer)
            {
                receiveBuffer.Clear();
            }
            registers.Reset();
        }

        public long Size
        {
            get
            {
                return 0x400;
            }
        }

        public GPIO IRQ { get; }

        public GPIO DMARecieve { get; }

        private uint HandleDataRead()
        {
            IRQ.Unset();
            lock(receiveBuffer)
            {
                if(receiveBuffer.TryDequeue(out var value))
                {
                    Update();
                    return value;
                }
                // We don't warn when the data register is read while it's empty because the HAL
                // (for example L0, F4) does this intentionally.
                // See https://github.com/STMicroelectronics/STM32CubeL0/blob/bec4e499a74de98ab60784bf2ef1912bee9c1a22/Drivers/STM32L0xx_HAL_Driver/Src/stm32l0xx_hal_spi.c#L1368-L1372
                return 0;
            }
        }

        private void HandleDataWrite(uint value)
        {
            IRQ.Unset();
            lock(receiveBuffer)
            {
                var peripheral = RegisteredPeripheral;
                if(peripheral == null)
                {
                    this.Log(LogLevel.Warning, "SPI transmission while no SPI peripheral is connected.");
                    receiveBuffer.Enqueue(0x0);
                    return;
                }
                var response = peripheral.Transmit((byte)value); // currently byte mode is the only one we support
                receiveBuffer.Enqueue(response);
                if(rxDmaEnable.Value)
                {
                    // This blink is used to signal the DMA that it should perform the peripheral -> memory transaction now
                    // Without this signal DMA will never move data from the receive buffer to memory
                    // See STM32DMA:OnGPIO
                    DMARecieve.Blink();
                }
                this.NoisyLog("Transmitted 0x{0:X}, received 0x{1:X}.", value, response);
            }
            Update();
        }

        private void Update()
        {
            var rxBufferNotEmpty = receiveBuffer.Count != 0;
            var rxBufferNotEmptyInterruptFlag = rxBufferNotEmpty && rxBufferNotEmptyInterruptEnable.Value;

            IRQ.Set(txBufferEmptyInterruptEnable.Value || rxBufferNotEmptyInterruptFlag);
        }

        private void DefineRegisters()
        {
            Registers.CR1.Define(registers)
                .WithFlag(15, name: "BIDIMODE")
                .WithFlag(14, name: "BIDIOE")
                .WithFlag(13, name: "CRCEN")
                .WithFlag(12, name: "CRCNEXT")
                .WithFlag(11, name: "CRCL")
                .WithFlag(10, name: "RXONLY")
                .WithFlag(9, name: "SSM")
                .WithFlag(8, name: "SSI")
                .WithFlag(7, name: "LSBFIRST")
                .WithFlag(6, out spiEnable, name: "SPE")
                .WithValueField(3, 3, name: "BR")
                .WithFlag(2, name: "MSTR")
                .WithFlag(1, name: "CPOL")
                .WithFlag(0, name: "CPHA");

            Registers.CR2.Define(registers)
                .WithReservedBits(15, 1)
                .WithFlag(14, name: "LDMA_TX")
                .WithFlag(13, name: "LDMA_RX")
                .WithFlag(12, name: "FRXTH")
                .WithValueField(8, 4, name: "DS")
                .WithFlag(7, name: "TXEIE")
                .WithFlag(6, name: "RXNEIE")
                .WithFlag(5, name: "ERRIE")
                .WithFlag(4, name: "FRF")
                .WithFlag(3, name: "NSSP")
                .WithFlag(2, name: "SSOE")
                .WithFlag(1, name: "TXDMAEN")
                .WithFlag(0, name: "RXDMAEN");

            Registers.SR.Define(registers)
                .WithValueField(11, 2, FieldMode.Read, valueProviderCallback: _ => 0UL, name: "FTLVL")
                .WithValueField(9, 2, FieldMode.Read, valueProviderCallback: _ => 0UL, name: "FRLVL")
                .WithFlag(8, FieldMode.Read, valueProviderCallback: _ => false, name: "FRE")
                .WithFlag(7, FieldMode.Read, valueProviderCallback: _ => false, name: "BSY")
                .WithFlag(6, FieldMode.Read, valueProviderCallback: _ => false, name: "OVR")
                .WithFlag(5, FieldMode.Read, valueProviderCallback: _ => false, name: "MODF")
                .WithFlag(4, FieldMode.Read, valueProviderCallback: _ => false, name: "CRCERR")
                .WithFlag(1, FieldMode.Read, valueProviderCallback: _ => true, name: "TXE")
                .WithFlag(0, FieldMode.Read, valueProviderCallback: _ => receiveBuffer.Count > 0, name: "RXNE");

            Registers.DR.Define(registers)
                .WithValueField(0, 16, writeCallback: (_, val) => HandleTransmit((byte)val),
                                     valueProviderCallback: _ => HandleReceive(), name: "DR");

            Registers.CRCPR.Define(registers)
                .WithTag("CRCPOLY", 0, 16);

            Registers.RXCRCR.Define(registers)
                .WithTag("RXCRC", 0, 16);

            Registers.TXCRCR.Define(registers)
                .WithTag("TXCRC", 0, 16);
        }

        private void HandleTransmit(byte value)
        {
            var peripheral = RegisteredPeripheral;
            byte response = peripheral?.Transmit(value) ?? (byte)0;
            receiveBuffer.Enqueue(response);
        }

        private byte HandleReceive()
        {
            return receiveBuffer.TryDequeue(out var val) ? val : (byte)0;
        }

        private IFlagRegisterField spiEnable;
        private DoubleWordRegisterCollection registers;
        private IFlagRegisterField txBufferEmptyInterruptEnable, rxBufferNotEmptyInterruptEnable, rxDmaEnable;
        private CircularBuffer<byte> receiveBuffer;

        private const int DefaultBufferCapacity = 64;

        private enum Registers
        {
            CR1 = 0x00,
            CR2 = 0x04,
            SR = 0x08,
            DR = 0x0C,
            CRCPR = 0x10,
            RXCRCR = 0x14,
            TXCRCR = 0x18,
        }
    }
}