//
// Copyright (c) 2010-2024 Antmicro
//
// This file is licensed under the MIT License.
// Full license text is available in 'licenses/MIT.txt'.
//
using System;
using System.Threading;
using Antmicro.Renode.Core;
using Antmicro.Renode.Logging;
using Antmicro.Renode.Peripherals.Bus;
using System.Collections.Generic;
using Antmicro.Migrant;
using Antmicro.Migrant.Hooks;
using Antmicro.Renode.Core.Structure.Registers;
using Antmicro.Renode.Time;

namespace Antmicro.Renode.Peripherals.UART
{
    [AllowedTranslations(AllowedTranslation.WordToDoubleWord | AllowedTranslation.ByteToDoubleWord)]
    public class STM32L4_UART : BasicDoubleWordPeripheral, IUART
    {
        public STM32L4_UART(IMachine machine, uint frequency = 16000000) : base(machine)
        {
            this.Log(LogLevel.Info, "STM32L4_UART constructor called!");
            this.frequency = frequency;
            DefineRegisters();
        }

        public void WriteChar(byte value)
        {
            if(!usartEnabled.Value && !receiverEnabled.Value)
            {
                this.Log(LogLevel.Warning, "Received a character, but the receiver is not enabled, dropping.");
                return;
            }
            receiveFifo.Enqueue(value);
            readFifoNotEmpty.Value = true;

            if(BaudRate == 0)
            {
                this.Log(LogLevel.Warning, "Unknown baud rate, couldn't trigger the idle line interrupt");
            }
            else
            {
                // Setup a timeout of 1 UART frame (8 bits) for Idle line detection
                idleLineDetectedCancellationTokenSrc?.Cancel();

                var idleLineIn = (8 * 1000000) / BaudRate;
                idleLineDetectedCancellationTokenSrc = new CancellationTokenSource();
                machine.ScheduleAction(TimeInterval.FromMicroseconds(idleLineIn), _ => ReportIdleLineDetected(idleLineDetectedCancellationTokenSrc.Token), name: $"{nameof(STM32L4_UART)} Idle line detected");
            }

            Update();
        }

        public override void Reset()
        {
            base.Reset();
            idleLineDetectedCancellationTokenSrc?.Cancel();
            receiveFifo.Clear();
            IRQ.Set(false);
        }

        public uint BaudRate
        {
            get
            {
                //OversamplingMode.By8 means we ignore the oldest bit of dividerFraction.Value
                var fraction = oversamplingMode.Value == OversamplingMode.By16 ? dividerFraction.Value : dividerFraction.Value & 0b111;

                var divisor = 8 * (2 - (int)oversamplingMode.Value) * (dividerMantissa.Value + fraction / 16.0);
                return divisor == 0 ? 0 : (uint)(frequency / divisor);
                // return 115200;
            }
        }

        public Bits StopBits
        {
            get
            {
                switch(stopBits.Value)
                {
                case StopBitsValues.Half:
                    return Bits.Half;
                case StopBitsValues.One:
                    return Bits.One;
                case StopBitsValues.OneAndAHalf:
                    return Bits.OneAndAHalf;
                case StopBitsValues.Two:
                    return Bits.Two;
                default:
                    throw new ArgumentException("Invalid stop bits value");
                }
            }
        }

        public Parity ParityBit => parityControlEnabled.Value ?
                                    (paritySelection.Value == ParitySelection.Even ?
                                        Parity.Even :
                                        Parity.Odd) :
                                    Parity.None;

        public GPIO IRQ { get; } = new GPIO();

        [field: Transient]
        public event Action<byte> CharReceived;

        private void DefineRegisters()
        {
            Register.CR1.Define(this, name: "USART_CR1")
                .WithFlag(0, out usartEnabled, name: "UE")
                .WithFlag(1, name: "UESM")
                .WithFlag(2, out receiverEnabled, name: "RE")
                .WithFlag(3, out transmitterEnabled, name: "TE")
                .WithFlag(4, out idleLineDetectedInterruptEnabled, name: "IDLEIE")
                .WithFlag(5, out receiverNotEmptyInterruptEnabled, name: "RXNEIE")
                .WithFlag(6, out transmissionCompleteInterruptEnabled, name: "TCIE")
                .WithFlag(7, out transmitDataRegisterEmptyInterruptEnabled, name: "TXEIE")
                .WithFlag(8, name: "PEIE")
                .WithEnumField(9, 1, out paritySelection, name: "PS")
                .WithFlag(10, out parityControlEnabled, name: "PCE")
                .WithFlag(11, name: "WAKE")
                .WithFlag(12, name: "M0")
                .WithFlag(13, name: "MME")
                .WithFlag(14, name: "CMIE")
                .WithEnumField(15, 1, out oversamplingMode, name: "OVER8")
                .WithValueField(16, 5, name: "DEDT", 
                    writeCallback: (_, __) => CheckUEBeforeWrite("DEDT"))
                .WithValueField(21, 5, name: "DEAT", 
                    writeCallback: (_, __) => CheckUEBeforeWrite("DEAT"))
                .WithFlag(26, name: "RTOIE")
                .WithFlag(27, name: "EOBIE")
                .WithFlag(28, name: "M1")
                .WithReservedBits(29, 3)
                .WithWriteCallback((_, __) =>
                {
                    if(!usartEnabled.Value || !receiverEnabled.Value)
                    {
                        idleLineDetectedCancellationTokenSrc?.Cancel();
                    }
                    Update();
                });

            Register.CR2.Define(this, name: "USART_CR2")
                .WithReservedBits(0, 4)
                .WithFlag(4, name: "ADDM7", writeCallback: (_, __) => CheckUEBeforeWrite("ADDM7"))        // 7-bit address detection
                .WithFlag(5, name: "LBDL", writeCallback: (_, __) => CheckUEBeforeWrite("LBDL"))           // LIN break detection length
                .WithFlag(6, name: "LBDIE", writeCallback: (_, __) => CheckUEBeforeWrite("LBDIE"))         // LIN break interrupt enable
                .WithReservedBits(7, 1)
                .WithFlag(8, name: "LBCL", writeCallback: (_, __) => CheckUEBeforeWrite("LBCL"))           // Last bit clock pulse
                .WithFlag(9, name: "CPHA", writeCallback: (_, __) => CheckUEBeforeWrite("CPHA"))           // Clock phase
                .WithFlag(10, name: "CPOL", writeCallback: (_, __) => CheckUEBeforeWrite("CPOL"))           // Clock polarity
                .WithFlag(11, name: "CLKEN", writeCallback: (_, __) => CheckUEBeforeWrite("CLKEN"))         // Clock enable
                .WithEnumField(12, 2, out stopBits, name: "STOP", writeCallback: (_, __) => CheckUEBeforeWrite("STOP")) // Stop bits
                .WithFlag(14, name: "LINEN", writeCallback: (_, __) => CheckUEBeforeWrite("LINEN"))        // LIN mode enable
                .WithFlag(15, name: "SWAP", writeCallback: (_, __) => CheckUEBeforeWrite("SWAP"))          // TX/RX swap
                .WithFlag(16, name: "RXINV", writeCallback: (_, __) => CheckUEBeforeWrite("RXINV"))        // RX pin inversion
                .WithFlag(17, name: "TXINV", writeCallback: (_, __) => CheckUEBeforeWrite("TXINV"))        // TX pin inversion
                .WithFlag(18, name: "DATAINV", writeCallback: (_, __) => CheckUEBeforeWrite("DATAINV"))    // Data bit inversion
                .WithFlag(19, name: "MSBFIRST", writeCallback: (_, __) => CheckUEBeforeWrite("MSBFIRST"))  // MSB first
                .WithFlag(20, out abren, name: "ABREN", writeCallback: (_, __) => CheckUEBeforeWrite("ABREN"))        // Auto baud rate enable
                .WithValueField(21, 2, name: "ABRMOD", writeCallback: (_, __) => CheckUEorABRENBeforeWrite("ABRMOD")) // Auto baud rate mode
                .WithFlag(23, name: "RTOEN", writeCallback: (_, __) => CheckUEBeforeWrite("RTOEN"))        // Receiver timeout enable
                .WithValueField(24, 4, name: "ADD[3:0]", writeCallback: (_, __) => CheckUEorREBeforeWrite("ADD[3:0]")) // Address LSB
                .WithValueField(28, 4, name: "ADD[7:4]", writeCallback: (_, __) => CheckUEorREBeforeWrite("ADD[7:4]")); // Address MSB

            Register.CR3.Define(this, name: "USART_CR3")
                .WithFlag(0, name: "EIE") // Error Interrupt Enable
                .WithFlag(1, name: "IREN", writeCallback: (_, __) => CheckUEBeforeWrite("IREN")) // IrDA Enable
                .WithFlag(2, name: "IRLP", writeCallback: (_, __) => CheckUEBeforeWrite("IRLP")) // IrDA Low Power
                .WithFlag(3, name: "HDSEL", writeCallback: (_, __) => CheckUEBeforeWrite("HDSEL")) // Half-duplex
                .WithFlag(4, name: "NACK", writeCallback: (_, __) => CheckUEBeforeWrite("NACK")) // NACK Enable
                .WithFlag(5, name: "SCEN", writeCallback: (_, __) => CheckUEBeforeWrite("SCEN")) // Smartcard Mode Enable
                .WithFlag(6, name: "DMAR") // DMA Enable Receiver
                .WithFlag(7, name: "DMAT") // DMA Enable Transmitter
                .WithFlag(8, name: "RTSE", writeCallback: (_, __) => CheckUEBeforeWrite("RTSE")) // RTS Enable
                .WithFlag(9, name: "CTSE", writeCallback: (_, __) => CheckUEBeforeWrite("CTSE")) // CTS Enable
                .WithFlag(10, name: "CTSIE") // CTS Interrupt Enable
                .WithFlag(11, name: "ONEBIT", writeCallback: (_, __) => CheckUEBeforeWrite("ONEBIT")) // One sample bit method
                .WithFlag(12, name: "OVRDIS", writeCallback: (_, __) => CheckUEBeforeWrite("OVRDIS")) // Overrun Disable
                .WithFlag(13, name: "DDRE", writeCallback: (_, __) => CheckUEBeforeWrite("DDRE")) // DMA Disable on Reception Error
                .WithFlag(14, name: "DEM", writeCallback: (_, __) => CheckUEBeforeWrite("DEM")) // Driver Enable Mode
                .WithFlag(15, name: "DEP", writeCallback: (_, __) => CheckUEBeforeWrite("DEP")) // Driver Enable Polarity
                .WithReservedBits(16, 1)
                .WithValueField(17, 1, name: "SCARCNT0", writeCallback: (_, val) =>
                {
                    if(usartEnabled.Value && val != 0)
                    {
                        this.Log(LogLevel.Warning, "SCARCNT0 must be 0 when UE=1 (USART enabled). Ignoring write.");
                    }
                })
                .WithValueField(18, 1, name: "SCARCNT1", writeCallback: (_, val) =>
                {
                    if(usartEnabled.Value && val != 0)
                    {
                        this.Log(LogLevel.Warning, "SCARCNT1 must be 0 when UE=1 (USART enabled). Ignoring write.");
                    }
                })
                .WithValueField(19, 1, name: "SCARCNT2", writeCallback: (_, val) =>
                {
                    if(usartEnabled.Value && val != 0)
                    {
                        this.Log(LogLevel.Warning, "SCARCNT2 must be 0 when UE=1 (USART enabled). Ignoring write.");
                    }
                })
                .WithValueField(20, 1, name: "WUS0", writeCallback: (_, __) => CheckUEBeforeWrite("WUS0")) // Wake-up from Stop flag select
                .WithValueField(21, 1, name: "WUS1", writeCallback: (_, __) => CheckUEBeforeWrite("WUS1")) // Wake-up from Stop flag select
                .WithFlag(22, name: "WUFIE") // Wake-up from Stop Interrupt Enable
                .WithFlag(23, name: "UCESM") // USART Clock Enable in Stop mode
                .WithFlag(24, name: "TCBGTIE") // Transmission complete before guard time
                .WithReservedBits(25, 7);

            Register.GTPR.Define(this, name: "USART_GTPR")
                .WithValueField(0, 8, name: "PSC", writeCallback: (_, val) =>
                {
                    if(usartEnabled.Value)
                    {
                        this.Log(LogLevel.Warning, "PSC can only be written when UE=0. Ignoring.");
                        return;
                    }
                    if(val == 0)
                    {
                        this.Log(LogLevel.Warning, "PSC value of 0 is reserved and must not be programmed.");
                    }
                    // Store/use value if needed
                })
                .WithValueField(8, 8, name: "GT", writeCallback: (_, __) =>
                {
                    if(usartEnabled.Value)
                    {
                        this.Log(LogLevel.Warning, "GT can only be written when UE=0. Ignoring.");
                    }
                    // Store/use value if needed
                })
                .WithReservedBits(16, 16);

            Register.ISR.Define(this, name: "USART_ISR")
                .WithFlag(0, out parityErrorFlag, FieldMode.Read, name: "PE")
                .WithFlag(1, FieldMode.Read, name: "FE")     // Framing error
                .WithFlag(2, FieldMode.Read, name: "NF")     // Noise detected
                .WithFlag(3, FieldMode.Read, name: "ORE")    // Overrun error
                .WithFlag(4, out idleLineDetected, FieldMode.Read, name: "IDLE") // Idle line
                .WithFlag(5, out readFifoNotEmpty, FieldMode.Read, name: "RXNE") // Read register not empty
                .WithFlag(6, out transmissionComplete, FieldMode.Read, name: "TC") // Transmission complete
                .WithFlag(7, FieldMode.Read, valueProviderCallback: _ => true, name: "TXE") // Transmit empty (always true for simplification)
                .WithFlag(8, FieldMode.Read, name: "LBDF")   // LIN break detected
                .WithFlag(9, FieldMode.Read, name: "CTSIF")  // CTS interrupt flag
                .WithFlag(10, FieldMode.Read, name: "CTS")   // CTS input level
                .WithFlag(11, FieldMode.Read, name: "RTOF")  // Receiver timeout
                .WithFlag(12, FieldMode.Read, name: "EOBF")  // End of block flag
                .WithReservedBits(13, 1)
                .WithFlag(14, FieldMode.Read, name: "ABRE")  // Auto baud error
                .WithFlag(15, FieldMode.Read, name: "ABRF")  // Auto baud flag
                .WithFlag(16, FieldMode.Read, name: "BUSY")  // Busy
                .WithFlag(17, FieldMode.Read, name: "CMF")   // Character match
                .WithFlag(18, FieldMode.Read, name: "SBKF")  // Send break flag
                .WithFlag(19, FieldMode.Read, name: "RWU")   // Mute mode
                .WithFlag(20, FieldMode.Read, name: "WUF")   // Wake-up from stop mode
                .WithFlag(21, FieldMode.Read, valueProviderCallback: _ => transmitterEnabled?.Value ?? false, name: "TEACK")// TX enable acknowledge                
                .WithFlag(22, FieldMode.Read, valueProviderCallback: _ => receiverEnabled?.Value ?? false, name: "REACK")   // RX enable acknowledge
                .WithReservedBits(23, 1)
                .WithReservedBits(24, 1)
                .WithFlag(25, FieldMode.Read, name: "TCBGT") // Transmission complete before guard time
                .WithReservedBits(26, 6);

            Register.RQR.Define(this, name: "USART_RQR")
                .WithFlag(0, FieldMode.Write, writeCallback: (_, val) =>
                {
                    if(val)
                    {
                        this.Log(LogLevel.Info, "ABRRQ: Auto baud rate request triggered. Flags ABRF and ABRE should be cleared (not implemented).");
                        // TODO: clear ABRF and ABRE in ISR if they exist
                    }
                }, name: "ABRRQ")
                .WithFlag(1, FieldMode.Write, writeCallback: (_, val) =>
                {
                    if(val)
                    {
                        this.Log(LogLevel.Info, "SBKRQ: Send break request triggered. TX break requested.");
                        // TODO: trigger break signal if needed
                    }
                }, name: "SBKRQ")
                .WithFlag(2, FieldMode.Write, writeCallback: (_, val) =>
                {
                    if(val)
                    {
                        this.Log(LogLevel.Info, "MMRQ: Mute mode request issued. RWU flag should be set.");
                        // TODO: set RWU bit in ISR if mute mode is implemented
                    }
                }, name: "MMRQ")
                .WithFlag(3, FieldMode.Write, writeCallback: (_, val) =>
                {
                    if(val)
                    {
                        this.Log(LogLevel.Info, "RXFRQ: Flush RX request. Clearing RXNE flag and discarding received data.");
                        receiveFifo.Clear();
                        readFifoNotEmpty.Value = false;
                        Update();
                    }
                }, name: "RXFRQ")
                .WithFlag(4, FieldMode.Write, writeCallback: (_, val) =>
                {
                    if(val)
                    {
                        this.Log(LogLevel.Info, "TXFRQ: Flush TX request. Setting TXE flag.");
                        transmissionComplete.Value = true;
                        Update();
                    }
                }, name: "TXFRQ")
                .WithReservedBits(5, 27);

            // Register.ISR.Define(this, name: "USART_ISR")
            //     .WithFlag(0, FieldMode.Read, valueProviderCallback: _ => parityErrorFlag, name: "PE")
            //     .WithFlag(1, FieldMode.Read, name: "FE")     // Framing error
            //     .WithFlag(2, FieldMode.Read, name: "NF")     // Noise detected
            //     .WithFlag(3, FieldMode.Read, name: "ORE")    // Overrun error
            //     .WithFlag(4, out idleLineDetected, FieldMode.Read, name: "IDLE") // Idle line
            //     .WithFlag(5, out readFifoNotEmpty, FieldMode.Read, name: "RXNE") // Read register not empty
            //     .WithFlag(6, out transmissionComplete, FieldMode.Read, name: "TC") // Transmission complete
            //     .WithFlag(7, FieldMode.Read, valueProviderCallback: _ => true, name: "TXE") // Transmit empty (always true for simplification)
            //     .WithFlag(8, FieldMode.Read, name: "LBDF")   // LIN break detected
            //     .WithFlag(9, FieldMode.Read, name: "CTSIF")  // CTS interrupt flag
            //     .WithFlag(10, FieldMode.Read, name: "CTS")   // CTS input level
            //     .WithFlag(11, FieldMode.Read, name: "RTOF")  // Receiver timeout
            //     .WithFlag(12, FieldMode.Read, name: "EOBF")  // End of block flag
            //     .WithReservedBits(13, 1)
            //     .WithFlag(14, FieldMode.Read, name: "ABRE")  // Auto baud error
            //     .WithFlag(15, FieldMode.Read, name: "ABRF")  // Auto baud flag
            //     .WithFlag(16, FieldMode.Read, name: "BUSY")  // Busy
            //     .WithFlag(17, FieldMode.Read, name: "CMF")   // Character match
            //     .WithFlag(18, FieldMode.Read, name: "SBKF")  // Send break flag
            //     .WithFlag(19, FieldMode.Read, name: "RWU")   // Mute mode
            //     .WithFlag(20, FieldMode.Read, name: "WUF")   // Wake-up from stop mode
            //     .WithFlag(21, FieldMode.Read, valueProviderCallback: _ => transmitterEnabled?.Value ?? false, name: "TEACK")// TX enable acknowledge                
            //     .WithFlag(22, FieldMode.Read, valueProviderCallback: _ => receiverEnabled?.Value ?? false, name: "REACK")   // RX enable acknowledge
            //     .WithReservedBits(23, 1)
            //     .WithReservedBits(24, 1)
            //     .WithFlag(25, FieldMode.Read, name: "TCBGT") // Transmission complete before guard time
            //     .WithReservedBits(26, 6);

            Register.ICR.Define(this, name: "USART_ICR")
                .WithFlag(0, FieldMode.WriteOneToClear, name: "PECF", writeCallback: (_, val) =>
                    {
                        if(val){
                            this.Log(LogLevel.Info, "Clearing PE (Parity Error) flag.");
                            parityErrorFlag.Value = false;
                            Update();
                        }
                    })
                    .WithFlag(1, FieldMode.WriteOneToClear, name: "FECF", writeCallback: (_, val) =>
                    {
                        if(val) this.Log(LogLevel.Info, "Clearing FE (Framing Error) flag.");
                    })
                    .WithFlag(2, FieldMode.WriteOneToClear, name: "NCF", writeCallback: (_, val) =>
                    {
                        if(val) this.Log(LogLevel.Info, "Clearing NF (Noise Detected) flag.");
                    })
                    .WithFlag(3, FieldMode.WriteOneToClear, name: "ORECF", writeCallback: (_, val) =>
                    {
                        if(val) this.Log(LogLevel.Info, "Clearing ORE (Overrun Error) flag.");
                    })
                    .WithFlag(4, FieldMode.WriteOneToClear, name: "IDLECF", writeCallback: (_, val) =>
                    {
                        if(val)
                        {
                            idleLineDetected.Value = false;
                            Update();
                            this.Log(LogLevel.Info, "Clearing IDLE flag.");
                        }
                    })
                    .WithReservedBits(5, 1)
                    .WithFlag(6, FieldMode.WriteOneToClear, name: "TCCF", writeCallback: (_, val) =>
                    {
                        if(val)
                        {
                            transmissionComplete.Value = false;
                            Update();
                            this.Log(LogLevel.Info, "Clearing TC (Transmission Complete) flag.");
                        }
                    })
                    .WithFlag(7, FieldMode.WriteOneToClear, name: "TCBGTCF", writeCallback: (_, val) =>
                    {
                        if(val) this.Log(LogLevel.Info, "Clearing TCBGT (Transmission Before Guard Time) flag.");
                    })
                    .WithFlag(8, FieldMode.WriteOneToClear, name: "LBDCF", writeCallback: (_, val) =>
                    {
                        if(val) this.Log(LogLevel.Info, "Clearing LBDF (LIN Break Detection) flag.");
                    })
                    .WithFlag(9, FieldMode.WriteOneToClear, name: "CTSCF", writeCallback: (_, val) =>
                    {
                        if(val) this.Log(LogLevel.Info, "Clearing CTSIF (CTS Interrupt) flag.");
                    })
                    .WithReservedBits(10, 1)
                    .WithFlag(11, FieldMode.WriteOneToClear, name: "RTOCF", writeCallback: (_, val) =>
                    {
                        if(val) this.Log(LogLevel.Info, "Clearing RTOF (Receiver Timeout) flag.");
                    })
                    .WithFlag(12, FieldMode.WriteOneToClear, name: "EOBCF", writeCallback: (_, val) =>
                    {
                        if(val) this.Log(LogLevel.Info, "Clearing EOBF (End of Block) flag.");
                    })
                    .WithReservedBits(13, 4)
                    .WithFlag(17, FieldMode.WriteOneToClear, name: "CMCF", writeCallback: (_, val) =>
                    {
                        if(val) this.Log(LogLevel.Info, "Clearing CMF (Character Match) flag.");
                    })
                    .WithReservedBits(18, 2)
                    .WithFlag(20, FieldMode.WriteOneToClear, name: "WUCF", writeCallback: (_, val) =>
                    {
                        if(val) this.Log(LogLevel.Info, "Clearing WUF (Wake-Up from Stop Mode) flag.");
                    })
                    .WithReservedBits(21, 11);

            Register.RDR.Define(this, name: "USART_RDR")
                .WithValueField(0, 9, FieldMode.Read, name: "RDR", valueProviderCallback: _ =>
                {
                    idleLineDetected.Value = false;

                    if(receiveFifo.Count == 0)
                    {
                        this.Log(LogLevel.Warning, "RDR read but FIFO was empty.");
                        readFifoNotEmpty.Value = false;
                        Update();
                        return 0u;
                    }

                    var value = (uint)receiveFifo.Dequeue();
                    readFifoNotEmpty.Value = receiveFifo.Count > 0;
                    Update();
                    return value;
                })
                .WithReservedBits(9, 23);

            Register.TDR.Define(this, name: "USART_TDR")
                .WithValueField(0, 9, FieldMode.Write, name: "TDR", writeCallback: (_, value) =>
                {
                    if(!usartEnabled.Value || !transmitterEnabled.Value)
                    {
                        this.Log(LogLevel.Warning, "TDR written but USART or transmitter not enabled. Dropping data.");
                        return;
                    }

                    var byteValue = (byte)(value & 0xFF); // Ignore MSB if parity is used
                    CharReceived?.Invoke(byteValue);

                    transmissionComplete.Value = true;
                    Update();

                    this.Log(LogLevel.Debug, $"TDR written: 0x{byteValue:X2}");
                })
                .WithReservedBits(9, 23);


            // Register.Status.Define(this, 0xC0, name: "USART_SR")
            //     .WithTaggedFlag("PE", 0)
            //     .WithTaggedFlag("FE", 1)
            //     .WithTaggedFlag("NF", 2)
            //     .WithFlag(3, FieldMode.Read, valueProviderCallback: _ => false, name: "ORE") // we assume no receive overruns
            //     .WithFlag(4, out idleLineDetected, FieldMode.Read, name: "IDLE")
            //     .WithFlag(5, out readFifoNotEmpty, FieldMode.Read | FieldMode.WriteZeroToClear, name: "RXNE") // as these two flags are WZTC, we cannot just calculate their results
            //     .WithFlag(6, out transmissionComplete, FieldMode.Read | FieldMode.WriteZeroToClear, name: "TC")
            //     .WithFlag(7, FieldMode.Read, valueProviderCallback: _ => true, name: "TXE") // we always assume "transmit data register empty"
            //     .WithTaggedFlag("LBD", 8)
            //     .WithTaggedFlag("CTS", 9)
            //     .WithReservedBits(10, 22)
            //     .WithWriteCallback((_, __) => Update())
            // ;
            // Register.Data.Define(this, name: "USART_DR")
            //     .WithValueField(0, 9, valueProviderCallback: _ =>
            //         {
            //             uint value = 0;

            //             // "Cleared by a USART_SR register followed by a read to the USART_DR register."
            //             // We can assume that USART_SR has already been read on the ISR.
            //             idleLineDetected.Value = false;

            //             if(receiveFifo.Count > 0)
            //             {
            //                 value = receiveFifo.Dequeue();
            //             }
            //             readFifoNotEmpty.Value = receiveFifo.Count > 0;
            //             Update();
            //             return value;
            //         }, writeCallback: (_, value) =>
            //         {
            //             if(!usartEnabled.Value && !transmitterEnabled.Value)
            //             {
            //                 this.Log(LogLevel.Warning, "Trying to transmit a character, but the transmitter is not enabled. dropping.");
            //                 return;
            //             }
            //             CharReceived?.Invoke((byte)value);
            //             transmissionComplete.Value = true;
            //             Update();
            //         }, name: "DR"
            //     )
            ;
            Register.BRR.Define(this, name: "USART_BRR")
                .WithValueField(0, 4, out dividerFraction, name: "BRR[3:0]", writeCallback: (ctx, val) =>
                {
                    if(oversamplingMode.Value == OversamplingMode.By8 && (val & 0b1000) != 0)
                    {
                        this.Log(LogLevel.Warning, "BRR[3] must be 0 when OVER8 = 1. Ignoring bit 3.");
                        val &= 0b0111; // Clear bit 3
                    }
                    dividerFraction.Value = val;
                })
                .WithValueField(4, 12, out dividerMantissa, name: "BRR[15:4]")
                .WithReservedBits(16, 16);

            // Register.Control1.Define(this, 0x0C, name: "USART_CR1")
            //     .WithTaggedFlag("SBK", 0)
            //     .WithTaggedFlag("RWU", 1)
            //     .WithFlag(2, out receiverEnabled, name: "RE")
            //     .WithFlag(3, out transmitterEnabled, name: "TE")
            //     .WithFlag(4, out idleLineDetectedInterruptEnabled, name: "IDLEIE")
            //     .WithFlag(5, out receiverNotEmptyInterruptEnabled, name: "RXNEIE")
            //     .WithFlag(6, out transmissionCompleteInterruptEnabled, name: "TCIE")
            //     .WithFlag(7, out transmitDataRegisterEmptyInterruptEnabled, name: "TXEIE")
            //     .WithTaggedFlag("PEIE", 8)
            //     .WithEnumField(9, 1, out paritySelection, name: "PS")
            //     .WithFlag(10, out parityControlEnabled, name: "PCE")
            //     .WithTaggedFlag("WAKE", 11)
            //     .WithTaggedFlag("M", 12)
            //     .WithFlag(13, out usartEnabled, name: "UE")
            //     .WithReservedBits(14, 1)
            //     .WithEnumField(15, 1, out oversamplingMode, name: "OVER8")
            //     .WithReservedBits(16, 16)
            //     .WithWriteCallback((_, val) =>
            //     {
            //         this.Log(LogLevel.Info, $"USART_CR1 write: 0x{val:X}, UE={usartEnabled.Value}, TE={transmitterEnabled.Value}, RE={receiverEnabled.Value}");
            //         if(!receiverEnabled.Value || !usartEnabled.Value)
            //         {
            //             idleLineDetectedCancellationTokenSrc?.Cancel();
            //         }
            //         Update();
            //     })
            //     ;
            // Register.Control2.Define(this, name: "USART_CR2")
            //     .WithTag("ADD", 0, 4)
            //     .WithReservedBits(5, 1)
            //     .WithTaggedFlag("LBDIE", 6)
            //     .WithReservedBits(7, 1)
            //     .WithTaggedFlag("LBCL", 8)
            //     .WithTaggedFlag("CPHA", 9)
            //     .WithTaggedFlag("CPOL", 10)
            //     .WithTaggedFlag("CLKEN", 11)
            //     .WithEnumField(12, 2, out stopBits, name: "STOP")
            //     .WithTaggedFlag("LINEN", 14)
            //     .WithReservedBits(15, 17)
            // ;
        }

        private void ReportIdleLineDetected(CancellationToken ct)
        {
            if(!ct.IsCancellationRequested)
            {
                idleLineDetected.Value = true;
                Update();
            }
        }

        private void Update()
        {
            IRQ.Set(
                (idleLineDetectedInterruptEnabled.Value && idleLineDetected.Value) ||
                (receiverNotEmptyInterruptEnabled.Value && readFifoNotEmpty.Value) ||
                (transmitDataRegisterEmptyInterruptEnabled.Value) || // TXE is assumed to be true
                (transmissionCompleteInterruptEnabled.Value && transmissionComplete.Value)
            );
        }

        private void CheckUEBeforeWrite(string field)
        {
            if(usartEnabled.Value)
            {
                this.Log(LogLevel.Warning, $"{field} can only be written when UE=0 (USART disabled). Ignoring.");
            }
        }

        private void CheckUEorREBeforeWrite(string field)
        {
            if(usartEnabled.Value || receiverEnabled.Value)
            {
                this.Log(LogLevel.Warning, $"{field} can only be written when both UE=0 and RE=0. Ignoring.");
            }
        }

        private void CheckUEorABRENBeforeWrite(string field)
        {
            if(usartEnabled.Value || abren.Value)
            {
                this.Log(LogLevel.Warning, $"{field} can only be written when ABREN=0 and UE=0. Ignoring.");
            }
        }

        private readonly uint frequency;

        private CancellationTokenSource idleLineDetectedCancellationTokenSrc;

        private IEnumRegisterField<OversamplingMode> oversamplingMode;
        private IEnumRegisterField<StopBitsValues> stopBits;
        private IFlagRegisterField usartEnabled;
        private IFlagRegisterField parityControlEnabled;
        private IEnumRegisterField<ParitySelection> paritySelection;
        private IFlagRegisterField transmissionCompleteInterruptEnabled;
        private IFlagRegisterField transmitDataRegisterEmptyInterruptEnabled;
        private IFlagRegisterField idleLineDetectedInterruptEnabled;
        private IFlagRegisterField receiverNotEmptyInterruptEnabled;
        private IFlagRegisterField receiverEnabled;
        private IFlagRegisterField transmitterEnabled;
        private IFlagRegisterField idleLineDetected;
        private IFlagRegisterField readFifoNotEmpty;
        private IFlagRegisterField transmissionComplete;

        private IFlagRegisterField parityErrorFlag;
        private IFlagRegisterField receiverTimeout;

        private IValueRegisterField dividerMantissa;
        private IValueRegisterField dividerFraction;

        private IFlagRegisterField abren;


        private readonly Queue<byte> receiveFifo = new Queue<byte>();

        private enum OversamplingMode
        {
            By16 = 0,
            By8 = 1
        }

        private enum StopBitsValues
        {
            One = 0,
            Half = 1,
            Two = 2,
            OneAndAHalf = 3
        }

        private enum ParitySelection
        {
            Even = 0,
            Odd = 1
        }

        private enum Register : long
        {
            // Status = 0x00,
            // Data = 0x04,
            // BaudRate = 0x08,
            // Control1 = 0x0C,
            // Control2 = 0x10,
            // Control3 = 0x14,
            // GuardTimeAndPrescaler = 0x18
            CR1 = 0x00,
            CR2 = 0x04,
            CR3 = 0x08,
            BRR = 0X0C,
            GTPR = 0x10,
            RTOR = 0x14,
            RQR = 0X18,
            ISR = 0x1C,
            ICR = 0x20,
            RDR = 0x24,
            TDR = 0x28
        }
    }
}
