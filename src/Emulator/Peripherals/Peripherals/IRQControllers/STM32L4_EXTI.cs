// Copyright (c) 2010-2025 Antmicro
// This file is licensed under the MIT License.
// Full license text is available in 'licenses/MIT.txt'.

using System;
using Antmicro.Renode.Peripherals.Bus;
using Antmicro.Renode.Core;
using Antmicro.Renode.Core.Structure.Registers;
using Antmicro.Renode.Logging;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Antmicro.Renode.Utilities;

namespace Antmicro.Renode.Peripherals.IRQControllers
{
    public class STM32L4_EXTI : BasicDoubleWordPeripheral, IKnownSize, IIRQController, INumberedGPIOOutput
    {
        public STM32L4_EXTI(IMachine machine, int numberOfOutputLines = 40) : base(machine)
        {
            var innerConnections = new Dictionary<int, IGPIO>();
            for(var i = 0; i < numberOfOutputLines; ++i)
            {
                innerConnections[i] = new GPIO();
            }
            Connections = new ReadOnlyDictionary<int, IGPIO>(innerConnections);

            core = new STM32_EXTICore(this, BitHelper.CalculateQuadWordMask(numberOfOutputLines - 1, 0));
            numberOfLinesMask = BitHelper.CalculateQuadWordMask(numberOfOutputLines - 1, 0);

            DefineRegisters();
            Reset();
        }

        public void OnGPIO(int number, bool value)
        {
            if(number >= Connections.Count)
            {
                this.Log(LogLevel.Error, "GPIO number {0} is out of range [0; {1})", number, Connections.Count);
                return;
            }
            var lineNumber = (byte)number;
            if(core.CanSetInterruptValue(lineNumber, value, out var isLineConfigurable))
            {
                value = isLineConfigurable ? true : value;
                core.UpdatePendingValue(lineNumber, value);
                Connections[number].Set(value);
            }
        }

        public override void Reset()
        {
            base.Reset();
            softwareInterrupt = 0;
            foreach(var gpio in Connections)
            {
                gpio.Value.Unset();
            }
        }

        public long Size => 0x400;

        public IReadOnlyDictionary<int, IGPIO> Connections { get; }

        private void DefineRegisters()
        {
            Registers.InterruptMask1.Define(this)
                .WithValueField(0, 32, out core.InterruptMask, name: "EXTI_IMR1");

            Registers.EventMask1.Define(this)
                .WithValueField(0, 32, name: "EXTI_EMR1");

            Registers.RisingTrigger1.Define(this)
                .WithValueField(0, 32, out core.RisingEdgeMask, name: "EXTI_RTSR1");

            Registers.FallingTrigger1.Define(this)
                .WithValueField(0, 32, out core.FallingEdgeMask, name: "EXTI_FTSR1");

            Registers.SoftwareInterrupt1.Define(this)
                .WithValueField(0, 32, name: "EXTI_SWIER1", valueProviderCallback: _ => softwareInterrupt,
                    writeCallback: (_, value) => {
                        value &= numberOfLinesMask;
                        BitHelper.ForeachActiveBit(value & core.InterruptMask.Value, x => Connections[x].Set());
                    });

            Registers.PendingRegister1.Define(this)
                .WithValueField(0, 32, out core.PendingInterrupts, FieldMode.Read | FieldMode.WriteOneToClear, name: "EXTI_PR1",
                    writeCallback: (_, value) => {
                        softwareInterrupt &= ~value;
                        value &= numberOfLinesMask;
                        BitHelper.ForeachActiveBit(value, x => Connections[x].Unset());
                    });
        }

        private ulong softwareInterrupt;
        private readonly ulong numberOfLinesMask;
        private readonly STM32_EXTICore core;

        private enum Registers
        {
            InterruptMask1 = 0x00,
            EventMask1 = 0x04,
            RisingTrigger1 = 0x08,
            FallingTrigger1 = 0x0C,
            SoftwareInterrupt1 = 0x10,
            PendingRegister1 = 0x14,
        }
    }
}