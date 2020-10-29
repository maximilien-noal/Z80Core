using System;
using System.IO;

using Z80core;

namespace MyApp
{
    public class Z80Exerciser : INotifyOps
    {
        private readonly MemIoOps memIo;

        private readonly Z80 z80;

        private readonly byte[] z80Ram = new byte[0x10000];

        private bool finish = false;

        public Z80Exerciser()
        {
            memIo = new MemIoOps(0, 0);
            memIo.SetRam(z80Ram);
            z80 = new Z80(memIo, this);
        }

        public static void Main(String[] args)
        {
            Z80Exerciser exerciser = new Z80Exerciser();
            DateTime start = DateTime.Now;
            exerciser.RunTest("zexall.bin");
            Console.WriteLine($"Test zexall.bin executed in {(start - DateTime.Now).TotalMilliseconds} ms.");
        }

        public int Breakpoint(int address, int opcode)
        {
            switch (z80.GetRegC())
            {
                case 0:
                    Console.WriteLine($"Z80 reset after {memIo.GetTstates()} t-states");
                    finish = true;
                    break;

                case 2:
                    Console.Write((char)z80.GetRegE());
                    break;

                case 9:
                    int strAddr = z80.GetRegDE();
                    while (z80Ram[strAddr] != '$')
                    {
                        Console.Write((char)z80Ram[strAddr++]);
                    }

                    break;

                default:
                    Console.WriteLine($"BDOS Call {z80.GetRegC()}");
                    finish = true;
                    break;
            }

            return opcode;
        }

        public void ExecDone()
        {
            throw new NotSupportedException("Not supported yet.");
        }

        private void RunTest(string testName)
        {
            try
            {
                var fileStream = File.OpenRead(testName);
                BufferedStream buffer = new BufferedStream(fileStream);
                int count = buffer.Read(z80Ram, 0x100, 0xFF00);
                Console.WriteLine($"Read {count} bytes from {testName}");
            }
            catch (IOException)
            {
                Console.WriteLine($"could not find test file : {testName}");
                return;
            }

            z80.Reset();
            memIo.Reset();
            finish = false;
            z80Ram[0] = (byte)0xC3;
            z80Ram[1] = 0x00;
            z80Ram[2] = 0x01;
            z80Ram[5] = (byte)0xC9;
            Console.WriteLine($"Starting test {testName}");
            z80.SetBreakpoint(0x0005, true);
            while (!finish)
            {
                z80.Execute();
            }

            Console.WriteLine($"Test {testName} ended.");
        }
    }
}