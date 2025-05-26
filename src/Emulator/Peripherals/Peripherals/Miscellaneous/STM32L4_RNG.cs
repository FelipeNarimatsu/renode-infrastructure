// Copyright (c) 2010-2024 Antmicro
//
// This file is licensed under the MIT License.
// Full license text is available in 'licenses/MIT.txt'.

using System;
using System.Collections.Generic;
using Antmicro.Renode.Core;
using Antmicro.Renode.Peripherals.Bus;
using Antmicro.Renode.Core.Structure.Registers;
using Antmicro.Renode.Logging;
using Antmicro.Renode.Utilities;

namespace Antmicro.Renode.Peripherals.Miscellaneous
{
    public class STM32L4_RNG : IDoubleWordPeripheral, IKnownSize
    {
        public STM32L4_RNG(IMachine machine)
        {
            IRQ = new GPIO();

            var registersMap = new Dictionary<long, DoubleWordRegister>
            {
                {(long)Registers.Control, new DoubleWordRegister(this)
                    .WithReservedBits(0, 2)
                    .WithFlag(2, out rngEnable, name: "RNGEN", changeCallback: (_, __) => Update())
                    .WithFlag(3, out interruptEnable, name: "IE", changeCallback: (_, __) => Update())
                    .WithReservedBits(4, 1)
                    .WithFlag(5, out clockErrorDetection, name: "CED")
                    .WithReservedBits(6, 26)
                },
                {(long)Registers.Status, new DoubleWordRegister(this)
                    .WithFlag(0, FieldMode.Read, valueProviderCallback: _ => rngEnable.Value, name: "DRDY")
                    .WithFlag(1, FieldMode.Read | FieldMode.WriteZeroToClear, name: "CECS")
                    .WithFlag(2, FieldMode.Read | FieldMode.WriteZeroToClear, name: "SECS")
                    .WithReservedBits(3, 2)
                    .WithFlag(5, FieldMode.Read | FieldMode.WriteZeroToClear, name: "CEIS")
                    .WithFlag(6, FieldMode.Read | FieldMode.WriteZeroToClear, name: "SEIS")
                    .WithReservedBits(7, 25)
                },
                {(long)Registers.Data, new DoubleWordRegister(this)
                    .WithValueField(0, 32, FieldMode.Read, valueProviderCallback: _ => rngEnable.Value ? GenerateRandom() : 0u, name: "RNDATA")
                },
            };

            registers = new DoubleWordRegisterCollection(this, registersMap);
        }

        public uint ReadDoubleWord(long offset)
        {
            return registers.Read(offset);
        }

        public void WriteDoubleWord(long offset, uint value)
        {
            registers.Write(offset, value);
        }

        public void Reset()
        {
            registers.Reset();
        }

        private uint GenerateRandom()
        {
            return unchecked((uint)rng.Next());
        }

        private void Update()
        {
            IRQ.Set(rngEnable.Value && interruptEnable.Value);
        }

        public GPIO IRQ { get; private set; }

        public long Size => 0x400;

        private readonly DoubleWordRegisterCollection registers;
        private readonly PseudorandomNumberGenerator rng = EmulationManager.Instance.CurrentEmulation.RandomGenerator;
        private IFlagRegisterField rngEnable;
        private IFlagRegisterField interruptEnable;
        private IFlagRegisterField clockErrorDetection;

        private enum Registers
        {
            Control = 0x0, // RNG_CR
            Status = 0x4,  // RNG_SR
            Data = 0x8     // RNG_DR
        }
    }
}
