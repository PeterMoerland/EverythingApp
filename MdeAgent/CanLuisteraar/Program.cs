// CanLuisteraar — 32-bit: GCAN USBCAN via ECanVci.dll (ZLG/ECAN-API). Elk ontvangen frame komt
// als JSON-regel op stdout; via stdin kunnen frames gestuurd worden (alleen in modus 0).
//
//   CanLuisteraar.exe <pad-naar-ECanVci.dll> <apparaattype> <apparaatindex> <kanaal> <kbit> <seconden> [modus]
//
//   modus 1 (standaard) = alleen luisteren (listen-only, zendt nooit, ook geen ACK)
//   modus 0             = normaal: ontvangen én zenden
//
// Uitvoer per frame:  {"t":1.234,"id":1409,"ext":0,"rtr":0,"data":"2b81600000000000"}
// Afsluitregel:       {"klaar":true,"frames":123}
// Fout:               {"error":"…"}  (exitcode 1)
//
// Invoer (stdin, per regel JSON):
//   {"id":1603,"data":"02320001"}   frame zenden (alleen modus 0); bevestiging: {"gezonden":1603,"ok":true}
//   {"stop":true}                    eerder stoppen

using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

[StructLayout(LayoutKind.Sequential)]
struct InitConfig
{
    public uint AccCode, AccMask, Reserved;
    public byte Filter, Timing0, Timing1, Mode;
}

[StructLayout(LayoutKind.Sequential)]
unsafe struct CanObj
{
    public uint ID, TimeStamp;
    public byte TimeFlag, SendType, RemoteFlag, ExternFlag, DataLen;
    public fixed byte Data[8];
    public fixed byte Reserved[3];
}

static class Native
{
    [DllImport("kernel32", SetLastError = true, CharSet = CharSet.Unicode)] public static extern IntPtr LoadLibrary(string path);
    [DllImport("kernel32", SetLastError = true)] public static extern IntPtr GetProcAddress(IntPtr module, string name);
}

[UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate uint OpenDeviceD(uint type, uint index, uint reserved);
[UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate uint CloseDeviceD(uint type, uint index);
[UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate uint InitCanD(uint type, uint index, uint can, ref InitConfig cfg);
[UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate uint StartCanD(uint type, uint index, uint can);
[UnmanagedFunctionPointer(CallingConvention.StdCall)] unsafe delegate uint ReceiveD(uint type, uint index, uint can, CanObj* buf, uint len, int wait);
[UnmanagedFunctionPointer(CallingConvention.StdCall)] unsafe delegate uint TransmitD(uint type, uint index, uint can, CanObj* buf, uint len);

static class Program
{
    static readonly Dictionary<int, (byte T0, byte T1)> Timing = new()
    {
        [1000] = (0x00, 0x14), [800] = (0x00, 0x16), [500] = (0x00, 0x1C), [250] = (0x01, 0x1C),
        [125] = (0x03, 0x1C), [100] = (0x04, 0x1C), [50] = (0x09, 0x1C), [20] = (0x18, 0x1C), [10] = (0x31, 0x1C),
    };

    static T Fn<T>(IntPtr lib, string naam) where T : Delegate
    {
        var p = Native.GetProcAddress(lib, naam);
        if (p == IntPtr.Zero) throw new Exception($"Functie {naam} niet gevonden in de DLL.");
        return Marshal.GetDelegateForFunctionPointer<T>(p);
    }

    // Opdrachten van stdin (zenden / stoppen), gelezen door een aparte thread.
    static readonly System.Collections.Concurrent.ConcurrentQueue<(uint Id, byte[] Data)> TeZenden = new();
    static volatile bool StopGevraagd;

    static void LeesStdin()
    {
        try
        {
            string? regel;
            while ((regel = Console.In.ReadLine()) is not null)
            {
                regel = regel.Trim();
                if (regel.Length == 0) continue;
                try
                {
                    using var doc = JsonDocument.Parse(regel);
                    var w = doc.RootElement;
                    if (w.TryGetProperty("stop", out var s) && s.ValueKind == JsonValueKind.True) { StopGevraagd = true; return; }
                    if (w.TryGetProperty("id", out var idEl))
                    {
                        var hex = w.TryGetProperty("data", out var d) ? d.GetString() ?? "" : "";
                        hex = hex.Replace(" ", "");
                        var data = Convert.FromHexString(hex);
                        if (data.Length > 8) throw new Exception("meer dan 8 bytes");
                        TeZenden.Enqueue((idEl.GetUInt32(), data));
                    }
                }
                catch (Exception ex)
                {
                    Console.Out.Write("{\"log\":" + JsonSerializer.Serialize("stdin-regel genegeerd: " + ex.Message) + "}\n");
                }
            }
        }
        catch { /* stdin dicht: gewoon doorgaan tot de tijd om is */ }
        // stdin gesloten door de agent = ook stoppen, anders blijft het proces hangen als de agent wegvalt
        StopGevraagd = true;
    }

    static unsafe int Main(string[] args)
    {
        var uit = new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false)) { AutoFlush = true };
        Console.SetOut(uit);
        try
        {
            if (args.Length < 6) throw new Exception("gebruik: CanLuisteraar <dll> <apparaattype> <index> <kanaal> <kbit> <seconden> [modus]");
            var dllPad = args[0];
            uint type = uint.Parse(args[1]), index = uint.Parse(args[2]), kanaal = uint.Parse(args[3]);
            int kbit = int.Parse(args[4]), seconden = int.Parse(args[5]);
            byte modus = args.Length > 6 ? byte.Parse(args[6]) : (byte)1;
            if (!Timing.TryGetValue(kbit, out var tm)) throw new Exception($"Onbekende bitrate {kbit}.");

            var lib = Native.LoadLibrary(dllPad);
            if (lib == IntPtr.Zero) throw new Exception($"DLL laden mislukt ({Marshal.GetLastWin32Error()}): {dllPad}");
            var open = Fn<OpenDeviceD>(lib, "OpenDevice");
            var close = Fn<CloseDeviceD>(lib, "CloseDevice");
            var init = Fn<InitCanD>(lib, "InitCAN");
            var start = Fn<StartCanD>(lib, "StartCAN");
            var receive = Fn<ReceiveD>(lib, "Receive");
            var transmit = Fn<TransmitD>(lib, "Transmit");

            if (open(type, index, 0) != 1) throw new Exception("OpenDevice mislukt: zit de USBCAN erin, driver geïnstalleerd, niet in gebruik door eTuner/eRob-Studio?");
            try
            {
                var cfg = new InitConfig { AccCode = 0, AccMask = 0xFFFFFFFF, Filter = 0, Timing0 = tm.T0, Timing1 = tm.T1, Mode = modus };
                if (init(type, index, kanaal, ref cfg) != 1) throw new Exception("InitCAN mislukt (kanaal/bitrate?).");
                if (start(type, index, kanaal) != 1) throw new Exception("StartCAN mislukt.");

                if (Console.IsInputRedirected)
                    new Thread(LeesStdin) { IsBackground = true }.Start();

                var buf = new CanObj[256];
                long frames = 0;
                var klok = System.Diagnostics.Stopwatch.StartNew();
                var sb = new StringBuilder();
                fixed (CanObj* p = buf)
                {
                    while (klok.Elapsed.TotalSeconds < seconden && !StopGevraagd)
                    {
                        // eerst zenden wat er ligt
                        while (TeZenden.TryDequeue(out var z))
                        {
                            var obj = new CanObj { ID = z.Id, SendType = 0, RemoteFlag = 0, ExternFlag = (byte)(z.Id > 0x7FF ? 1 : 0), DataLen = (byte)z.Data.Length };
                            for (var k = 0; k < z.Data.Length; k++) obj.Data[k] = z.Data[k];
                            uint ok;
                            if (modus == 0) ok = transmit(type, index, kanaal, &obj, 1);
                            else ok = 0;
                            uit.Write("{\"gezonden\":" + z.Id + ",\"ok\":" + (ok == 1 ? "true" : "false")
                                      + ",\"t\":" + klok.Elapsed.TotalSeconds.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture)
                                      + (modus != 0 ? ",\"reden\":\"listen-only\"" : "") + "}\n");
                        }

                        var n = receive(type, index, kanaal, p, 256, 20);
                        if (n == 0 || n == 0xFFFFFFFF) continue;
                        var t = klok.Elapsed.TotalSeconds;
                        for (var i = 0; i < n; i++)
                        {
                            var f = p[i];
                            frames++;
                            sb.Clear();
                            for (var k = 0; k < f.DataLen && k < 8; k++) sb.Append(f.Data[k].ToString("x2"));
                            uit.Write("{\"t\":"); uit.Write(t.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture));
                            uit.Write(",\"id\":"); uit.Write(f.ID);
                            uit.Write(",\"ext\":"); uit.Write(f.ExternFlag);
                            uit.Write(",\"rtr\":"); uit.Write(f.RemoteFlag);
                            uit.Write(",\"data\":\""); uit.Write(sb); uit.Write("\"}\n");
                        }
                    }
                }
                uit.Write("{\"klaar\":true,\"frames\":" + frames + ",\"gestopt\":" + (StopGevraagd ? "true" : "false") + "}\n");
            }
            finally { close(type, index); }
            return 0;
        }
        catch (Exception ex)
        {
            uit.Write("{\"error\":" + JsonSerializer.Serialize(ex.Message) + "}\n");
            return 1;
        }
    }
}
