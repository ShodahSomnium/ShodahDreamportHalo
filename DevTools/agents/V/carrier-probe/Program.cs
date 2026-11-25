using System;
static class Program
{
    static int Main(string[] a)
    {
        string k = a.Length > 0 ? a[0] : "";
        if (k == "pic") return MapPic.Run(a);
        if (k == "col") return Col.Run(a);
        if (k == "dump") return Dump.Run(a);
        if (k == "drop") return Drop.Run(a);
        if (k == "all") return AllScan.Run(a);
        if (k == "hang") return Hang.Run(a);
        return Carrier.Run(a);
    }
}
