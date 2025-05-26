//
// Copyright (c) 2010-2023 Antmicro
// Copyright (c) 2011-2015 Realtime Embedded
//
// This file is licensed under the MIT License.
// Full license text is available in 'licenses/MIT.txt'.
//
using System;
using Antmicro.Renode.Core;
using Antmicro.Renode.Core.Structure;
using Antmicro.Renode.Logging;
using Antmicro.Renode.Peripherals.Bus;
using System.Collections.Generic;
using System.Linq;
using Antmicro.Renode.Core.Structure.Registers;
using Antmicro.Renode.Utilities;

namespace Antmicro.Renode.Peripherals.I2C
{
    [AllowedTranslations(AllowedTranslation.WordToDoubleWord)]
    public sealed class STM32L4_I2C : SimpleContainer<II2CPeripheral>, IDoubleWordPeripheral, IBytePeripheral, IKnownSize
    {
        public STM32L4_I2C(IMachine machine) : base(machine)
        {
            EventInterrupt = new GPIO();
            ErrorInterrupt = new GPIO();
            CreateRegisters();
            Reset();
        }

        public byte ReadByte(long offset)
        {
            if((Registers)offset == Registers.Data)
            {
                byteTransferFinished.Value = false;
                Update();
                return (byte)data.Read();
            }
            this.LogUnhandledRead(offset);
            return 0;
        }

        public void WriteByte(long offset, byte value)
        {
            if((Registers)offset == Registers.Data)
            {
                data.Write(offset, value);
            }
            else
            {
                this.LogUnhandledWrite(offset, value);
            }
        }

        public uint ReadDoubleWord(long offset) => registers.Read(offset);
        public void WriteDoubleWord(long offset, uint value) => registers.Write(offset, value);

        public override void Reset()
        {
            state = State.Idle;
            EventInterrupt.Unset();
            ErrorInterrupt.Unset();
            registers.Reset();
            data.Reset();
        }

        public GPIO EventInterrupt { get; private set; }
        public GPIO ErrorInterrupt { get; private set; }
        public long Size => 0x400;

        private void CreateRegisters()
        {
            var control1 = new DoubleWordRegister(this)
                .WithFlag(15, writeCallback: SoftwareResetWrite, name: "SWRST")
                .WithFlag(10, out acknowledgeEnable,   name: "ACK")
                .WithFlag(9,  FieldMode.Read, writeCallback: StopWrite, name: "STOP")
                .WithFlag(8,  FieldMode.Read, writeCallback: StartWrite,name: "START")
                .WithFlag(0,  out peripheralEnable, writeCallback: PeripheralEnableWrite, name: "PE");

            var control2 = new DoubleWordRegister(this)
                .WithValueField(0, 6, name: "FREQ")
                .WithFlag(10, out bufferInterruptEnable, changeCallback: InterruptEnableChange, name: "BUF_IE")
                .WithFlag(9,  out eventInterruptEnable,  changeCallback: InterruptEnableChange, name: "EVT_IE")
                .WithFlag(8,  out errorInterruptEnable,                        name: "ERR_IE");

            var status1 = new DoubleWordRegister(this)
                .WithFlag(10, out acknowledgeFailed, FieldMode.ReadToClear | FieldMode.WriteZeroToClear, changeCallback: (_,__) => Update(), name: "AF")
                .WithFlag(7,  out dataRegisterEmpty, FieldMode.Read,                                       name: "TXE")
                .WithFlag(6,  out dataRegisterNotEmpty, FieldMode.Read, valueProviderCallback: _ => dataToReceive?.Any() ?? false, name: "RXNE")
                .WithFlag(2,  out byteTransferFinished, FieldMode.Read,                                      name: "BTF")
                .WithFlag(1,  out addressSentOrMatched, FieldMode.Read,                                   name: "ADDR")
                .WithFlag(0,  out startBit,           FieldMode.Read,                                      name: "SB");

            var status2 = new DoubleWordRegister(this)
                .WithFlag(2, out transmitterReceiver, FieldMode.Read, name: "TRA")
                .WithFlag(0, FieldMode.Read, readCallback: (_,__) => { addressSentOrMatched.Value = false; Update(); }, name: "MSL");

            data = new DoubleWordRegister(this)
                .WithValueField(0, 8, out dataRegister,
                    name: "DR",
                    valueProviderCallback: prev => DataRead((uint)prev),
                    writeCallback:           (prev, val) => DataWrite((uint)prev, (uint)val));

            var map = new Dictionary<long, DoubleWordRegister>
            {
                {(long)Registers.Control1,   control1},
                {(long)Registers.Control2,   control2},
                {(long)Registers.OwnAddress1, DoubleWordRegister.CreateRWRegister()},
                {(long)Registers.OwnAddress2, DoubleWordRegister.CreateRWRegister()},
                {(long)Registers.Data,        data},
                {(long)Registers.Status1,     status1},
                {(long)Registers.Status2,     status2},
                {(long)Registers.ClockControl, DoubleWordRegister.CreateRWRegister()},
                {(long)Registers.RiseTime,     DoubleWordRegister.CreateRWRegister(0x2)},
                {(long)Registers.NoiseFilter,  DoubleWordRegister.CreateRWRegister()},
            };
            registers = new DoubleWordRegisterCollection(this, map);
        }

        private void InterruptEnableChange(bool oldValue, bool newValue) => machine.LocalTimeSource.ExecuteInNearestSyncedState(_ => Update());

        private void Update()
        {
            EventInterrupt.Set(
                eventInterruptEnable.Value
                && (startBit.Value
                    || addressSentOrMatched.Value
                    || byteTransferFinished.Value
                    || (bufferInterruptEnable.Value
                        && (dataRegisterEmpty.Value || dataRegisterNotEmpty.Value)))
            );
            ErrorInterrupt.Set(errorInterruptEnable.Value && acknowledgeFailed.Value);
        }

        private uint DataRead(uint oldValue)
        {
            uint result = 0;
            if(dataToReceive?.Any() ?? false)
            {
                result = dataToReceive.Dequeue();
            }
            else
            {
                this.Log(LogLevel.Warning, "DR empty");
            }
            byteTransferFinished.Value = dataToReceive?.Any() ?? false;
            Update();
            return result;
        }

        private void DataWrite(uint oldValue, uint newValue)
        {
            byteTransferFinished.Value = false;
            Update();
            switch(state)
            {
                case State.AwaitingAddress:
                    startBit.Value = false;
                    willReadOnSelectedSlave = (newValue & 1) == 1;
                    var address = (int)(newValue >> 1);
                    if(ChildCollection.ContainsKey(address))
                    {
                        selectedSlave = ChildCollection[address];
                        addressSentOrMatched.Value = true;
                        transmitterReceiver.Value    = !willReadOnSelectedSlave;
                        if(willReadOnSelectedSlave)
                        {
                            dataToReceive = new Queue<byte>(selectedSlave.Read());
                            byteTransferFinished.Value = true;
                        }
                        else
                        {
                            state          = State.AwaitingData;
                            dataToTransfer = new List<byte>();
                            dataRegisterEmpty.Value = true;
                            addressSentOrMatched.Value = true;
                        }
                    }
                    else
                    {
                        state = State.Idle;
                        acknowledgeFailed.Value = true;
                    }
                    machine.LocalTimeSource.ExecuteInNearestSyncedState(_ => Update());
                    break;

                case State.AwaitingData:
                    dataToTransfer.Add((byte)newValue);
                    machine.LocalTimeSource.ExecuteInNearestSyncedState(_ =>
                    {
                        dataRegisterEmpty.Value    = true;
                        byteTransferFinished.Value = true;
                        Update();
                    });
                    break;

                default:
                    this.Log(LogLevel.Warning, "Bad write {0} in {1}", newValue, state);
                    break;
            }
        }

        private void SoftwareResetWrite(bool oldValue, bool newValue) { if(newValue) Reset(); }
        private void StopWrite(bool oldValue, bool newValue)
        {
            this.NoisyLog("STOP={0}", newValue);
            if(!newValue) return;
            if(selectedSlave != null && dataToTransfer?.Count > 0)
            {
                selectedSlave.Write(dataToTransfer.ToArray());
                dataToTransfer.Clear();
            }
            state = State.Idle;
            byteTransferFinished.Value = false;
            dataRegisterEmpty.Value    = false;
            Update();
        }
        private void StartWrite(bool oldValue, bool newValue)
        {
            if(!newValue) return;
            this.NoisyLog("START={0}", newValue);
            if(selectedSlave != null && dataToTransfer?.Count > 0)
            {
                selectedSlave.Write(dataToTransfer.ToArray());
                dataToTransfer.Clear();
            }
            transmitterReceiver.Value    = false;
            dataRegisterEmpty.Value      = false;
            byteTransferFinished.Value   = false;
            startBit.Value               = true;
            if(state == State.Idle || state == State.AwaitingData)
            {
                state = State.AwaitingAddress;
                masterSlave.Value = true;
                Update();
            }
        }
        private void PeripheralEnableWrite(bool oldValue, bool newValue)
        {
            if(!newValue)
            {
                acknowledgeEnable.Value   = false;
                masterSlave.Value         = false;
                acknowledgeFailed.Value   = false;
                transmitterReceiver.Value = false;
                dataRegisterEmpty.Value   = false;
                byteTransferFinished.Value= false;
                Update();
            }
        }

        // Fields
        private IFlagRegisterField peripheralEnable;
        private IFlagRegisterField acknowledgeEnable;
        private IFlagRegisterField bufferInterruptEnable, eventInterruptEnable, errorInterruptEnable;
        private IValueRegisterField dataRegister;
        private IFlagRegisterField acknowledgeFailed, dataRegisterEmpty, dataRegisterNotEmpty, byteTransferFinished, addressSentOrMatched, startBit;
        private IFlagRegisterField transmitterReceiver, masterSlave;

        private DoubleWordRegister data;
        private DoubleWordRegisterCollection registers;

        private State state;
        private List<byte> dataToTransfer;
        private Queue<byte> dataToReceive;
        private bool willReadOnSelectedSlave;
        private II2CPeripheral selectedSlave;

        private enum Registers : long
        {
            Control1     = 0x0,
            Control2     = 0x4,
            OwnAddress1  = 0x8,
            OwnAddress2  = 0xC,
            Data         = 0x10,
            Status1      = 0x14,
            Status2      = 0x18,
            ClockControl = 0x1C,
            RiseTime     = 0x20,
            NoiseFilter  = 0x24,
        }

        private enum State
        {
            Idle,
            AwaitingAddress,
            AwaitingData,
        }
    }
}
