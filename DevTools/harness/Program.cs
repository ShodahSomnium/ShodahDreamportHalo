using System;

static class Program
{
    static int Main(string[] a)
    {
        if (a.Length == 0) { Console.WriteLine("commands: defs <dir> <out>"); return 1; }
        switch (a[0])
        {
            case "defs": Defs.Run(a[1], a[2]); return 0;
        }
        return Probe.Run(a);
    }
}
