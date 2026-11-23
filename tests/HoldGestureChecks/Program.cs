using VirtualTourBox.Models;
using VirtualTourBox.Services;
long time=0;
var output=new List<string>();
var engine=new MappingEngine {Enabled=true,ClockMs=()=>time,HoldDelayMs=400,RepeatDelayMs=500,RepeatIntervalMs=150,
    SendIpadKey=k=>{output.Add(k);return true;},
    SendIpadCombo=k=>{output.Add(k);return true;},
    SetIpadSmudge=(down,legacy)=>{output.Add(down?(legacy?"m+":"alt+"):(legacy?"m-":"alt-"));return true;}
};
void Feed(int elapsed,params string[] keys){time+=elapsed;engine.Update(keys.ToHashSet());}
void Start(params (string key,string action)[] entries)
{
    engine.Reset();output.Clear();
    engine.Profile=new MappingProfile {Target="iPad",Bindings=entries.ToDictionary(p=>p.key,p=>new MappingBinding{Action=p.action})};
    Feed(0);
}
void Expect(params string[] expected)
{if(!output.SequenceEqual(expected))throw new Exception("Expected "+string.Join(",",expected)+" got "+string.Join(",",output));}
Start(("X","Brush"),("X.Hold","Smudge"));
Feed(0,"X");Expect();Feed(399,"X");Expect();Feed(0);Expect("b");
Feed(0);Expect("b");
Start(("X","Brush"),("X.Hold","Smudge"));
Feed(0,"X");Feed(400,"X");Feed(200,"X");Expect("alt+");
Feed(0);Expect("alt+","alt-");Feed(0);Expect("alt+","alt-");
Start(("Y","Eraser"),("Y.Hold","TemporaryEraser"));
Feed(0,"Y");Feed(100);Expect("e");
output.Clear();Feed(0,"Y");Feed(400,"Y");Feed(10);Expect("e","b");
Console.WriteLine("X/Y taps and holds, exact threshold and release suppression passed.");
Start(("X","Brush"),("X.Hold","Smudge"));
Feed(0,"X");Feed(400,"X");engine.Reset();engine.Reset();Expect("alt+","alt-");
Feed(10,"X");Feed(600,"X");Expect("alt+","alt-");Feed(0);Feed(0,"X");Feed(400,"X");Expect("alt+","alt-","alt+");
engine.Enabled=false;Feed(0,"X");Expect("alt+","alt-","alt+","alt-");
engine.Enabled=true;Feed(500,"X");Expect("alt+","alt-","alt+","alt-");Feed(0);
Console.WriteLine("Pause/reset/disconnect release and held-key rearm guard passed.");
Start(("A","Redo"),("A.Hold","RedoRepeat"),("B","Undo"),("B.Hold","UndoRepeat"));
Feed(0,"A");Expect("gui+shift+z");Feed(499,"A");Expect("gui+shift+z");
Feed(1,"A");Expect("gui+shift+z","gui+shift+z");Feed(149,"A");Expect("gui+shift+z","gui+shift+z");
Feed(1,"A");Feed(0);Feed(500);Expect("gui+shift+z","gui+shift+z","gui+shift+z");
output.Clear();Feed(0,"B");Feed(500,"B");Feed(0);Expect("gui+z","gui+z");
engine.RepeatDelayMs=100;engine.RepeatIntervalMs=50;
output.Clear();Feed(0,"B");Feed(99,"B");Expect("gui+z");Feed(1,"B");Feed(50,"B");Feed(0);Expect("gui+z","gui+z","gui+z");
Console.WriteLine("Independent A/B history pairs preserve immediate tap and custom repeat timings.");
Start(("X","Brush"),("X.Double","Layers"),("X.Hold","Smudge"));
Feed(0,"X");Feed(50);Expect();Feed(300);Expect();Feed(1);Expect("b");
output.Clear();Feed(0,"X");Feed(20);Feed(50,"X");Feed(20);Expect("l");Feed(301);Expect("l");
output.Clear();Feed(0,"X");Feed(400,"X");Feed(0);Feed(301);Expect("alt+","alt-");
Console.WriteLine("Single/double/hold arbitration passed.");
Start(("X","Brush"),("X.Hold","Smudge"),("L+X","Layers"));
Feed(0,"X");Feed(100,"L","X");Feed(500,"X");Feed(0);Expect("l");
output.Clear();Feed(0,"X");Feed(400,"X");Feed(0,"L","X");Feed(0);Expect("alt+","alt-","l");
Console.WriteLine("Combo consumes pending click/hold and releases active tool.");
Start(("X","Brush"),("X.Hold","Smudge"),("Y","Eraser"),("Y.Hold","TemporaryEraser"));
Feed(0,"X");Feed(400,"X");Feed(0,"X","Y");Feed(400,"X","Y");Feed(0,"X");Feed(0);
Expect("alt+","alt-","e","b","alt+","alt-");
Console.WriteLine("Simultaneous held-tool priority and release passed.");
// Persistence must preserve all gesture variants and unknown compatible bindings.
var saved=System.Text.Json.JsonSerializer.Serialize(engine.Profile);
var restored=System.Text.Json.JsonSerializer.Deserialize<MappingProfile>(saved)!;
if(restored.Bindings["X.Hold"].Action!="Smudge"||restored.Bindings["Y.Hold"].Action!="TemporaryEraser")throw new Exception("Persistence lost hold");
Console.WriteLine("Gesture JSON round trip passed.");
Start(("X","Brush"),("X.Hold","CustomKey"));
engine.Profile!.Target="Windows";engine.Profile.Bindings["X.Hold"].Shortcut="K";
KeyboardOutput.Record=k=>output.Add("win:"+k);
Feed(0,"X");Feed(100);Expect("win:B");
output.Clear();Feed(0,"X");Feed(400,"X");Feed(0);Expect("win:K");
Start(("Y","Eraser"),("Y.Hold","TemporaryEraser"));engine.Profile!.Target="Windows";
Feed(0,"Y");Feed(400,"Y");engine.Reset();Expect("win:E","win:B");
Console.WriteLine("Windows tap/hold and temporary-tool release routes passed (stubbed output).");
Start(("X","Brush"),("X.Hold","SmudgeLegacy"));
Feed(0,"X");Feed(400,"X");Feed(0);Expect("m+","m-");
Console.WriteLine("Legacy held-smudge mode preserved.");
namespace VirtualTourBox.Services {
    public static class KeyboardOutput {
        public static Action<string>? Record;
        public static string SendShortcut(string value) {Record?.Invoke(value);return "已注入";}
    }
}
