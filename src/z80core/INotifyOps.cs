namespace Z80core
{
    public interface INotifyOps
    {
        int Breakpoint(int address, int opcode);

        void ExecDone();
    }
}