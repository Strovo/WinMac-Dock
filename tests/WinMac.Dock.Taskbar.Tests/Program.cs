using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using WinMac.Dock;
internal static class Program
{
    [StructLayout(LayoutKind.Sequential)] struct FLASH { public uint Size; public nint Window; public uint Flags,Count,Timeout; }
    [DllImport("user32.dll")] static extern bool FlashWindowEx(ref FLASH value);
    [DllImport("user32.dll")] static extern nint GetWindowLongPtrW(nint h,int index);
    [DllImport("user32.dll")] static extern bool GetLayeredWindowAttributes(nint h,out uint key,out byte alpha,out uint flags);
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(nint h);
    record Appearance(long Handle,bool Visible,bool Layered,uint Key,byte Alpha,uint Flags)
    { internal bool Drawn=>Visible&&!(Layered&&(Flags&2)!=0&&Alpha==0); }
    static Appearance Read(nint h) {
        bool layered=(GetWindowLongPtrW(h,-20).ToInt64()&0x80000)!=0;
        uint key=0,flags=0;byte alpha=255;
        if(layered&&!GetLayeredWindowAttributes(h,out key,out alpha,out flags))throw new Exception("Cannot sample layered attributes");
        return new(h.ToInt64(),Native.IsWindowVisible(h),layered,key,alpha,flags);
    }
    static List<nint> Bars() {var bars=new List<nint>();Native.EnumWindows((h,_)=>{if(Native.Class(h) is "Shell_TrayWnd" or "Shell_SecondaryTrayWnd")bars.Add(h);return true;},0);return bars.OrderBy(h=>h.ToInt64()).ToList();}
    [STAThread] static int Main(string[] args)
    {
        Log.DirectoryPath=Path.GetFullPath(args[0]);Directory.CreateDirectory(Log.DirectoryPath);
        if(args.Contains("--recover")) {
            using var recovery=new WindowsTaskbar();recovery.Recover();
            File.WriteAllText(Path.Combine(Log.DirectoryPath,"recovered.json"),JsonSerializer.Serialize(Bars().Select(Read)));
            return recovery.Error==null?0:1;
        }
        if(args.Contains("--sample")) {
            File.WriteAllText(Path.Combine(Log.DirectoryPath,"sample.json"),JsonSerializer.Serialize(Bars().Select(Read)));
            return 0;
        }
        var samples=new List<object>();var checks=new List<string>();var original=Bars().Select(Read).ToArray();
        var emergencyRestore=Bars().Select(TaskbarOpacity.Capture).ToArray();
        bool external=args.Contains("--external");
        bool revealed=false;Exception? failure=null;int flashRequests=0;
        using var taskbar=new WindowsTaskbar();
        void Check(bool ok,string text){if(!ok)throw new Exception(text);checks.Add(text);}
        try {
            Check(original.Length==Screen.AllScreens.Length,"Taskbar on every monitor");
            if(!external) taskbar.Sync(true); Check(taskbar.Error==null,"Hide preparation: "+taskbar.Error);
            if(!args.Contains("--opacity-probe")) Check(Bars().Select(Read).All(b=>b.Layered&&b.Alpha==0&&(b.Flags&2)!=0),"Persistent zero-alpha suppression is established before flashing");
            if(args.Contains("--crash")) {
                File.WriteAllText(Path.Combine(Log.DirectoryPath,"before-crash.json"),JsonSerializer.Serialize(original));
                Environment.Exit(23); // Real process termination, no Dispose/finally.
            }
            if(args.Contains("--lifecycle")) {
                string journalPath=Path.Combine(Log.DirectoryPath,"taskbar-visibility.json");
                string journal=File.ReadAllText(journalPath);
                using(var doc=JsonDocument.Parse(journal)) Check(doc.RootElement.GetProperty("Opacity").EnumerateObject().Count()==original.Length,"Opacity serialized for every taskbar");
                foreach(var h in Bars())Native.ShowWindow(h,8);
                Check(Bars().Select(Read).All(b=>!b.Drawn),"Explorer WS_VISIBLE re-show cannot render either taskbar");
                taskbar.Sync(true);
                using(var temporary=StartTaskbarAppearance.Acquire(Bars())) { }
                Check(Bars().Select(Read).All(b=>b.Layered&&b.Alpha==0),"Closing nested Start restores permanent alpha zero");
                using(var temporary=StartTaskbarAppearance.Acquire(Bars())) { taskbar.Sync(false); }
                Check(original.SequenceEqual(Bars().Select(Read)),"Hide off during nested Start restores original appearance");
                taskbar.Sync(true);
                using(var recovery=new WindowsTaskbar()) recovery.Recover();
                Check(original.SequenceEqual(Bars().Select(Read)),"Crash recovery restores serialized original appearance");
                Check(!File.Exists(journalPath),"Successful recovery removes journal");
                // Old visibility/configuration-only journals remain readable.
                var old=System.Text.Json.Nodes.JsonNode.Parse(journal)!;old.AsObject().Remove("Opacity");
                File.WriteAllText(journalPath,old.ToJsonString());
                using(var recovery=new WindowsTaskbar()) recovery.Recover();
                Check(!File.Exists(journalPath),"Legacy recovery journal without opacity remains valid");
                taskbar.Sync(false);taskbar.Dispose();
                using(var temporary=StartTaskbarAppearance.Acquire(Bars())) { taskbar.Sync(true); }
                Check(Bars().Select(Read).All(b=>b.Layered&&b.Alpha==0),"Hide on while Start is open stays transparent after Start closes");
                taskbar.Sync(false);taskbar.Dispose();
                Check(original.SequenceEqual(Bars().Select(Read)),"Hide on during Start preserves true original appearance");
                using(var owned=new Form {Text="Owned opacity recovery fixture",Opacity=.61,TransparencyKey=System.Drawing.Color.Magenta}) {
                    owned.Show();Application.DoEvents();
                    var captured=TaskbarOpacity.Capture(owned.Handle);
                    Check(captured.WasLayered&&captured.Alpha>0&&captured.Alpha<255,"Owned fixture has nondefault layered attributes");
                    var serialized=JsonSerializer.Deserialize<TaskbarOpacity>(JsonSerializer.Serialize(captured))!;
                    serialized.Suppress();Check(!Read(owned.Handle).Drawn,"Owned fixture suppression applied");
                    (serialized with {ProcessStart=serialized.ProcessStart+1}).Restore();
                    Check(!Read(owned.Handle).Drawn,"Stale Explorer process identity is never restored");
                    Check(TaskbarOpacity.Permanent(owned.Handle)==serialized,"Stale identity cannot release the live permanent owner");
                    serialized.Restore();
                    Check(TaskbarOpacity.Capture(owned.Handle)==captured,"Exact pre-existing color key, alpha and flags survive serialization and restore");
                }
                taskbar.Sync(true);
                Native.FailPositionRead=true;
                taskbar.Dispose();
                Native.FailPositionRead=false;
                Check(original.SequenceEqual(Bars().Select(Read)),"Opacity restores even when Explorer configuration restoration fails");
                Check(File.Exists(journalPath),"Failed configuration restore retains complete recovery journal");
                taskbar.Dispose();
                Check(!File.Exists(journalPath),"Configuration recovery can retry after shell read failure");
                Check(!Directory.EnumerateFiles(Log.DirectoryPath,"*.tmp-*").Any(),"Atomic recovery publication leaves no temporary files");
            }
            else {
            using var opacityProbe=args.Contains("--opacity-probe")?StartTaskbarAppearance.Acquire(Bars()):null;
            using var foreground=new Form{Text="Attention test foreground (owned)",Width=260,Height=100};foreground.Show();
            foreach(var screen in Screen.AllScreens) {
                using var fixture=new Form{Text="WinMac real FlashWindowEx fixture "+screen.DeviceName,StartPosition=FormStartPosition.Manual,Bounds=new(screen.Bounds.X+120,screen.Bounds.Y+120,340,130)};
                fixture.Show();Application.DoEvents();foreground.Activate();SetForegroundWindow(foreground.Handle);Application.DoEvents();
                var flash=new FLASH{Size=(uint)Marshal.SizeOf<FLASH>(),Window=fixture.Handle,Flags=3|12,Count=uint.MaxValue,Timeout=100};
                // FLASHW_ALL | FLASHW_TIMERNOFG; leave the request active for 12 seconds.
                FlashWindowEx(ref flash);flashRequests++;
                var timer=Stopwatch.StartNew();long nextSync=0,nextSample=0;int drawnSamples=0,visibleSamples=0;
                while(timer.ElapsedMilliseconds<12000) {
                    Application.DoEvents();var bars=Bars().Select(Read).ToArray();
                    if(bars.Any(b=>b.Visible))visibleSamples++;
                    if(bars.Any(b=>b.Drawn)){revealed=true;drawnSamples++;}
                    if(timer.ElapsedMilliseconds>=nextSample||bars.Any(b=>b.Drawn)) {samples.Add(new{monitor=screen.DeviceName,ms=timer.ElapsedMilliseconds,bars});nextSample=timer.ElapsedMilliseconds+50;}
                    if(!external&&timer.ElapsedMilliseconds>=nextSync){taskbar.Sync(true);nextSync=timer.ElapsedMilliseconds+100;}
                    Thread.Sleep(5);
                }
                flash.Flags=0;FlashWindowEx(ref flash);
                Console.WriteLine($"{screen.DeviceName}: duration=12000ms drawnSamples={drawnSamples} visibleSamples={visibleSamples}");
            }
            Check(flashRequests==Screen.AllScreens.Length,"Real FlashWindowEx invoked on each monitor");
            Check(!revealed,"Taskbars remain visually hidden for the entire flashing interval");
            }
        } catch(Exception ex){failure=ex;Console.Error.WriteLine(ex.Message);}
        finally {
            if(!external) { taskbar.Sync(false);taskbar.Dispose(); }
            var after=Bars().Select(Read).ToArray();
            bool restored=external||original.SequenceEqual(after);
            if(!restored&&failure==null)failure=new Exception("Original opacity/visibility did not restore");
            if(!restored) foreach(var snapshot in emergencyRestore) {
                snapshot.Restore();
                Native.ShowWindow((nint)snapshot.Handle,original.Single(b=>b.Handle==snapshot.Handle).Visible?8:0);
            }
            checks.Add("Original opacity/visibility restored="+restored);
            File.WriteAllText(Path.Combine(Log.DirectoryPath,"attention.json"),JsonSerializer.Serialize(new{Passed=failure==null,Error=failure?.ToString(),flashRequests,revealed,original,after,checks,samples},new JsonSerializerOptions{WriteIndented=true}));
        }
        return failure==null?0:1;
    }
}
