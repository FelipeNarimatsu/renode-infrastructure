﻿// Copyright (c) 2010-2025 Antmicro
//
// This file is licensed under the MIT License.
// Full license text is available in 'licenses/MIT.txt'.

using System;
using System.Collections.Generic;
using Antmicro.Renode.Core;
using Antmicro.Renode.Core.Structure.Registers;
using Antmicro.Renode.Peripherals.Bus;
using Antmicro.Renode.Peripherals.Timers;

namespace Antmicro.Renode.Peripherals.Miscellaneous
{
    [AllowedTranslations(AllowedTranslation.ByteToDoubleWord | AllowedTranslation.WordToDoubleWord)]
    public sealed class STM32L4_RCC : IDoubleWordPeripheral, IKnownSize, IProvidesRegisterCollection<DoubleWordRegisterCollection>
    {
        public STM32L4_RCC(IMachine machine, STM32L4_RTC rtcPeripheral)
        {            
            this.machine = machine;
            this.rtcPeripheral = rtcPeripheral;

            registers = new DoubleWordRegisterCollection(this);
            DefineRegisters();
        }

        public void Reset() => registers.Reset();

        public uint ReadDoubleWord(long offset) => registers.Read(offset);

        public void WriteDoubleWord(long offset, uint value) => registers.Write(offset, value);

        public long Size => 0x400;

        public DoubleWordRegisterCollection Registers => registers;
        public DoubleWordRegisterCollection RegistersCollection => registers;

        private void DefineRegisters()
        {
            // Reset registers
            Registers.DefineRegister((long)Register.AHB1RSTR)
                .WithFlag(0, name: "DMA1RST")
                .WithFlag(1, name: "DMA2RST")
                .WithFlag(2, name: "DMAMUX1RST")
                .WithReservedBits(3, 5)
                .WithFlag(8, name: "FLASHRST")
                .WithFlag(9, name: "CRCRST")
                .WithReservedBits(10, 22);
            Registers.DefineRegister((long)Register.AHB2RSTR)
                .WithFlag(0, name: "GPIOARST")
                .WithFlag(1, name: "GPIOBRST")
                .WithFlag(2, name: "GPIOCRST")
                .WithFlag(3, name: "GPIODRST")
                .WithFlag(4, name: "GPIOERST")
                .WithFlag(5, name: "GPIOFRST")
                .WithFlag(6, name: "GPIOGRST")
                .WithReservedBits(7, 3)
                .WithFlag(10, name: "ADC_RST")
                .WithReservedBits(11, 21);
            Registers.DefineRegister((long)Register.AHB3RSTR)
                .WithFlag(0, name: "FMCRST")
                .WithFlag(1, name: "OSPI1RST")
                .WithReservedBits(2, 30);
            Registers.DefineRegister((long)Register.APB1RSTR1)
                .WithFlag(0, name: "TIM2RST")
                .WithFlag(1, name: "TIM3RST")
                .WithFlag(2, name: "TIM4RST")
                .WithFlag(3, name: "TIM5RST")
                .WithFlag(4, name: "TIM6RST")
                .WithFlag(5, name: "TIM7RST")
                .WithFlag(6, name: "LCDRST")
                .WithFlag(7, name: "RTCAPBRST")
                .WithFlag(8, name: "WWDGRST")
                .WithReservedBits(9, 2)
                .WithFlag(11, name: "SPI2RST")
                .WithFlag(12, name: "SPI3RST")
                .WithFlag(13, name: "USART2RST")
                .WithFlag(14, name: "USART3RST")
                .WithFlag(15, name: "UART4RST")
                .WithFlag(16, name: "UART5RST")
                .WithFlag(17, name: "I2C1RST")
                .WithFlag(18, name: "I2C2RST")
                .WithFlag(19, name: "I2C3RST")
                .WithReservedBits(20, 2)
                .WithFlag(22, name: "CRSRST")
                .WithFlag(23, name: "CAN1RST")
                .WithReservedBits(24, 8);
            Registers.DefineRegister((long)Register.APB1RSTR2)
                .WithFlag(0, name: "LPUART1RST")
                .WithFlag(1, name: "I2C4RST")
                .WithReservedBits(2, 30);
            Registers.DefineRegister((long)Register.APB2RSTR)
                .WithFlag(0, name: "SYSCFGRST")
                .WithReservedBits(1, 10)
                .WithFlag(11, name: "SDMMC1RST")
                .WithFlag(12, name: "TIM1RST")
                .WithFlag(13, name: "SPI1RST")
                .WithFlag(14, name: "TIM8RST")
                .WithFlag(15, name: "USART1RST")
                .WithReservedBits(16, 16);

            // Enable registers
            Registers.DefineRegister((long)Register.AHB1ENR)
                .WithFlag(0, name: "DMA1EN")
                .WithFlag(1, name: "DMA2EN")
                .WithFlag(2, name: "DMAMUX1EN")
                .WithReservedBits(3, 5)
                .WithFlag(8, name: "FLASHEN")
                .WithFlag(9, name: "CRCEN")
                .WithReservedBits(10, 22);
            Registers.DefineRegister((long)Register.AHB2ENR)
                .WithFlag(0, name: "GPIOAEN")
                .WithFlag(1, name: "GPIOBEN")
                .WithFlag(2, name: "GPIOCEN")
                .WithFlag(3, name: "GPIODEN")
                .WithFlag(4, name: "GPIOEEN")
                .WithFlag(5, name: "GPIOFEN")
                .WithFlag(6, name: "GPIOGEN")
                .WithReservedBits(7, 3)
                .WithFlag(10, name: "ADCEN")
                .WithReservedBits(11, 21);
            Registers.DefineRegister((long)Register.AHB3ENR)
                .WithFlag(0, name: "FMCEN")
                .WithFlag(1, name: "OSPI1EN")
                .WithReservedBits(2, 30);
            Registers.DefineRegister((long)Register.APB1ENR1)
                .WithFlag(0, name: "TIM2EN")
                .WithFlag(1, name: "TIM3EN")
                .WithFlag(2, name: "TIM4EN")
                .WithFlag(3, name: "TIM5EN")
                .WithFlag(4, name: "TIM6EN")
                .WithFlag(5, name: "TIM7EN")
                .WithReservedBits(6, 3)
                .WithFlag(9, name: "LCDEN")
                .WithFlag(10, name: "RTCAPBEN")
                .WithFlag(11, name: "WWDGEN")
                .WithReservedBits(12, 2)
                .WithFlag(14, name: "SPI2EN")
                .WithFlag(15, name: "SPI3EN")
                .WithReservedBits(16, 1)
                .WithFlag(17, name: "USART2EN")
                .WithFlag(18, name: "USART3EN")
                .WithFlag(19, name: "UART4EN")                
                .WithFlag(20, name: "UART5EN")
                .WithFlag(21, name: "I2C1EN")
                .WithFlag(22, name: "I2C2EN")
                .WithFlag(23, name: "I2C3EN")
                .WithFlag(24, name: "CRSEN")
                .WithFlag(25, name: "CAN1EN")
                .WithFlag(26, name: "CAN2EN")
                .WithReservedBits(27, 1)
                .WithFlag(28, name: "PWREN")
                .WithFlag(29, name: "DAC1EN")
                .WithFlag(30, name: "OPAMPEN")
                .WithFlag(31, name: "LPTIM1EN");
            Registers.DefineRegister((long)Register.APB1ENR2)
                .WithFlag(0, name: "LPUART1EN")
                .WithFlag(1, name: "I2C4EN")
                .WithReservedBits(2, 30);
            Registers.DefineRegister((long)Register.APB2ENR)
                .WithFlag(0, name: "SYSCFGEN")
                .WithReservedBits(1, 10)
                .WithFlag(11, name: "SDMMC1EN")
                .WithFlag(12, name: "TIM1EN")
                .WithFlag(13, name: "SPI1EN")
                .WithFlag(14, name: "TIM8EN")
                .WithFlag(15, name: "USART1EN")
                .WithReservedBits(16, 16);

            Registers.DefineRegister((long)Register.CCIPR)
                .WithValueField(0, 2, name: "USART1SEL")
                .WithValueField(2, 2, name: "USART2SEL")
                .WithValueField(4, 2, name: "USART3SEL")
                .WithValueField(6, 2, name: "UART4SEL")
                .WithValueField(8, 2, name: "UART5SEL")
                .WithValueField(10, 2, name: "LPUART1SEL")
                .WithValueField(12, 2, name: "I2C1SEL")
                .WithValueField(14, 2, name: "I2C2SEL")
                .WithValueField(16, 2, name: "I2C3SEL")
                .WithValueField(18, 2, name: "LPTIM1SEL")
                .WithValueField(20, 2, name: "LPTIM2SEL")
                .WithValueField(22, 2, name: "SAI1SEL")
                .WithValueField(24, 2, name: "SAI2SEL")
                .WithValueField(26, 2, name: "CLK48SEL")
                .WithValueField(28, 2, name: "ADCSEL")
                .WithFlag(30, name: "SWPMI1SEL")
                .WithFlag(31, name: "DFSDM1SEL");

            Registers.DefineRegister((long)Register.CR, 0x63)
                .WithFlag(0, name: "MSION")
                .WithFlag(1, FieldMode.Read, name: "MSIRDY")
                .WithFlag(2, name: "MSIPLLEN")
                .WithFlag(3, name: "MSIRGSEL")
                .WithValueField(4, 4, name: "MSIRANGE")
                .WithFlag(8, name: "HSION")
                .WithFlag(9, FieldMode.Read, name: "HSIRDY")
                .WithFlag(10, name: "HSIKERON")
                .WithFlag(11, name: "HSIASFS")
                .WithReservedBits(12, 4)
                .WithFlag(16, name: "HSEON")
                .WithFlag(17, FieldMode.Read, name: "HSERDY")
                .WithFlag(18, name: "HSEBYP")
                .WithFlag(19, name: "CSSON")
                .WithReservedBits(20, 4)
                .WithFlag(24, name: "PLLON")
                .WithFlag(25, FieldMode.Read, name: "PLLRDY")
                .WithFlag(26, name: "PLLSAI1ON")
                .WithFlag(27, FieldMode.Read, name: "PLLSAI1RDY")
                .WithFlag(28, name: "PLLSAI2ON")
                .WithFlag(29, FieldMode.Read, name: "PLLSAI2RDY")
                .WithReservedBits(30, 2);

            Registers.DefineRegister((long)Register.CFGR)
                .WithValueField(0, 2, name: "SW")
                .WithValueField(2, 2, FieldMode.Read, name: "SWS")
                .WithValueField(4, 4, name: "HPRE")
                .WithValueField(8, 3, name: "PPRE1")
                .WithValueField(11, 3, name: "PPRE2")
                .WithFlag(15, name: "STOPWUCK")
                .WithReservedBits(16, 8)
                .WithValueField(24, 4, name: "MCOSEL")
                .WithValueField(28, 3, name: "MCOPRE")
                .WithReservedBits(31, 1);

            Registers.DefineRegister((long)Register.BDCR)
                    .WithFlag(15, name: "RTCEN",
                        writeCallback: (_, value) =>
                        {
                            if(value)
                            {
                                machine.SystemBus.EnablePeripheral(rtcPeripheral);
                            }
                            else
                            {
                                machine.SystemBus.DisablePeripheral(rtcPeripheral);
                            }
                        });

            Registers.DefineRegister((long)Register.ICSCR)
                .WithTag("MSICAL", 0, 8)
                .WithTag("MSITRIM", 8, 8)
                .WithTag("HSICAL", 16, 8)
                .WithTag("HSITRIM", 24, 7)
                .WithReservedBits(31, 1);

            Registers.DefineRegister((long)Register.PLLCFGR)
                .WithTag("PLLSRC", 0, 2)
                .WithTag("PLLM", 4, 3)
                .WithTag("PLLN", 8, 7)
                .WithTag("PLLPEN", 16, 1)
                .WithTag("PLLP", 17, 1)
                .WithTag("PLLQEN", 20, 1)
                .WithTag("PLLQ", 21, 2)
                .WithTag("PLLREN", 24, 1)
                .WithTag("PLLR", 25, 2)
                .WithReservedBits(27, 5);

            Registers.DefineRegister((long)Register.PLLSAI1CFGR)
                .WithTag("PLLSAI1N", 8, 7)
                .WithTag("PLLSAI1PEN", 16, 1)
                .WithTag("PLLSAI1P", 17, 1)
                .WithTag("PLLSAI1QEN", 20, 1)
                .WithTag("PLLSAI1Q", 21, 2)
                .WithTag("PLLSAI1REN", 24, 1)
                .WithTag("PLLSAI1R", 25, 2)
                .WithReservedBits(27, 5);

            Registers.DefineRegister((long)Register.PLLSAI2CFGR)
                .WithTag("PLLSAI2N", 8, 7)
                .WithTag("PLLSAI2PEN", 16, 1)
                .WithTag("PLLSAI2P", 17, 1)
                .WithTag("PLLSAI2REN", 24, 1)
                .WithTag("PLLSAI2R", 25, 2)
                .WithReservedBits(27, 5);

            Registers.DefineRegister((long)Register.CIER)
                .WithTag("LSIRDYIE", 0, 1)
                .WithTag("LSERDYIE", 1, 1)
                .WithTag("HSIRDYIE", 2, 1)
                .WithTag("HSERDYIE", 3, 1)
                .WithTag("PLLRDYIE", 4, 1)
                .WithTag("PLLSAI1RDYIE", 5, 1)
                .WithTag("PLLSAI2RDYIE", 6, 1)
                .WithTag("MSIRDYIE", 7, 1)
                .WithReservedBits(8, 24);

            Registers.DefineRegister((long)Register.CIFR)
                .WithTag("LSIRDYF", 0, 1)
                .WithTag("LSERDYF", 1, 1)
                .WithTag("HSIRDYF", 2, 1)
                .WithTag("HSERDYF", 3, 1)
                .WithTag("PLLRDYF", 4, 1)
                .WithTag("PLLSAI1RDYF", 5, 1)
                .WithTag("PLLSAI2RDYF", 6, 1)
                .WithTag("MSIRDYF", 7, 1)
                .WithReservedBits(8, 24);

            Registers.DefineRegister((long)Register.CICR)
                .WithTag("LSIRDYC", 0, 1)
                .WithTag("LSERDYC", 1, 1)
                .WithTag("HSIRDYC", 2, 1)
                .WithTag("HSERDYC", 3, 1)
                .WithTag("PLLRDYC", 4, 1)
                .WithTag("PLLSAI1RDYC", 5, 1)
                .WithTag("PLLSAI2RDYC", 6, 1)
                .WithTag("MSIRDYC", 7, 1)
                .WithReservedBits(8, 24);

            Registers.DefineRegister((long)Register.CSR)
                .WithTag("LSION", 0, 1)
                .WithTag("LSIRDY", 1, 1)
                .WithReservedBits(2, 2)
                .WithTag("RMVF", 23, 1)
                .WithTag("OBLRSTF", 25, 1)
                .WithTag("PINRSTF", 26, 1)
                .WithTag("BORRSTF", 27, 1)
                .WithTag("SFTRSTF", 28, 1)
                .WithTag("IWDGRSTF", 29, 1)
                .WithTag("WWDGRSTF", 30, 1)
                .WithTag("LPWRRSTF", 31, 1);

            Registers.DefineRegister((long)Register.CRRCR)
                .WithTag("HSI48ON", 0, 1)
                .WithTag("HSI48RDY", 1, 1)
                .WithReservedBits(2, 5)
                .WithTag("HSI48CAL", 7, 9)
                .WithReservedBits(16, 16);

            Registers.DefineRegister((long)Register.CCIPR2)
                .WithTag("I2C4SEL", 0, 2)
                .WithTag("DFSDM1SEL", 2, 2)
                .WithTag("ADCPSEL", 4, 2)
                .WithReservedBits(6, 26);
        }

        private readonly DoubleWordRegisterCollection registers;

        private readonly IMachine machine;
        private readonly STM32L4_RTC rtcPeripheral;

        private enum Register : long
        {
            CR = 0x00,
            ICSCR = 0x04,
            CFGR = 0x08,
            PLLCFGR = 0x0C,
            PLLSAI1CFGR = 0x10,
            PLLSAI2CFGR = 0x14,
            CIER = 0x18,
            CIFR = 0x1C,
            CICR = 0x20,
            AHB1RSTR = 0x28,
            AHB2RSTR = 0x2C,
            AHB3RSTR = 0x30,
            APB1RSTR1 = 0x38,
            APB1RSTR2 = 0x3C,
            APB2RSTR = 0x40,
            AHB1ENR = 0x48,
            AHB2ENR = 0x4C,
            AHB3ENR = 0x50,
            APB1ENR1 = 0x58,
            APB1ENR2 = 0x5C,
            APB2ENR = 0x60,
            CCIPR = 0x88,
            BDCR = 0x90,
            CSR = 0x94,
            CRRCR = 0x98,
            CCIPR2 = 0x9C
        }
    }
}