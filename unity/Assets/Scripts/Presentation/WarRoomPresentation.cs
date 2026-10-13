#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Statecraft.Game.Map;
using Statecraft.Game.UI;
using Statecraft.Sim;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace Statecraft.Game.Presentation
{
    /// <summary>Local, opt-in Living War Room. Everything is a view of SimBridge.State. Commands route through the
    /// original UI's buttons; this class never queues orders, steps, or changes simulation state.</summary>
    public sealed class WarRoomPresentation : IDisposable
    {
        public const string StylesheetResource = "WarRoom";
        readonly VisualElement document, root, overlay, header, mapLayer, originalBar, officePortrait, officeInsignia;
        readonly SimBridge bridge; readonly MapScene map; readonly CameraRig rig; readonly PortraitCatalog? portraits;
        readonly StyleEnum<Visibility> originalVisibility; readonly float originalInset;
        readonly StyleSheet? sheet; readonly WarRoomMapVisuals visuals; readonly WarRoomAudio audio; readonly WarRoomDriver driver;
        readonly Label date, report, briefTitle, briefSummary, nextAction, dispatchTitle;
        readonly Button endTurn, sound, skip;
        readonly List<(Label value, Label delta)> resourceLabels = new List<(Label, Label)>();
        readonly List<(Label title, Label detail)> goalLabels = new List<(Label, Label)>();
        readonly List<Label> tokenLabels = new List<Label>();
        readonly List<(Label label, Vector3 point)> callouts = new List<(Label, Vector3)>();
        readonly List<VisualElement> pings = new List<VisualElement>();
        GameState opening; GameSession session; WarRoomModel model = null!;
        int drawnTurn = -1, drawnPlacements = -1; bool disposed, drama;
        float elapsed, beforeDistance, beforeYaw; Vector3 beforeLookAt;
        const float DramaSeconds = 4.6f;

        public static bool Requested() => Requested(Application.absoluteURL, Environment.GetCommandLineArgs(), Application.platform==RuntimePlatform.WebGLPlayer);
        public static bool Requested(string? url, string[] args, bool webgl)
        {
            if(!webgl) { int i=Array.IndexOf(args,"-experience"); return i>=0 && i+1<args.Length && args[i+1]=="war-room"; }
            var address=(url??"").Split('#')[0]; int start=address.IndexOf('?');
            var query=start<0?"":address.Substring(start+1);
            return Regex.IsMatch(query,@"(?:^|&)experience=war-room(?=&|$)",RegexOptions.CultureInvariant);
        }

        public WarRoomPresentation(GameObject host, VisualElement root, SimBridge bridge, MapScene map, CameraRig rig, PortraitCatalog? portraits=null)
        {
            if(host==null) throw new ArgumentNullException(nameof(host));
            document=root??throw new ArgumentNullException(nameof(root)); this.root=root.Q("root")??root;
            this.bridge=bridge??throw new ArgumentNullException(nameof(bridge)); this.map=map; this.rig=rig; this.portraits=portraits;
            session=bridge.Session; opening=bridge.State.Clone(); originalInset=rig!=null?rig.TopInset:0;
            originalBar=root.Q("top-bar"); originalVisibility=originalBar.style.visibility;
            // Visibility keeps the measured 96 px top-bar geometry available to TopBarInset.
            originalBar.style.visibility=Visibility.Hidden;
            sheet=Resources.Load<StyleSheet>(StylesheetResource); if(sheet!=null) this.root.styleSheets.Add(sheet);
            this.root.AddToClassList("war-room-active");
            overlay=Box("war-room","war-room");
            if(originalBar.parent==this.root)this.root.Insert(this.root.IndexOf(originalBar),overlay);else this.root.Add(overlay);
            mapLayer=Box("war-map-layer","war-map-layer"); mapLayer.pickingMode=PickingMode.Ignore; overlay.Add(mapLayer);
            header=Box("war-header","war-header"); header.pickingMode=PickingMode.Position; overlay.Add(header);
            var identity=Box("war-identity","war-identity"); header.Add(identity);
            identity.Add(Text("STATECRAFT : ISRAEL","war-eyebrow")); identity.Add(Text("LIVING WAR ROOM","war-brand"));
            date=Text("","war-date"); identity.Add(date);
            var resources=Box("war-resources","war-resources"); header.Add(resources);
            string[] icons={"IL","$","+","+","O","!"}; string[] captions={"TREASURY","FOREIGN CURRENCY","ARRIVALS / TURN","SECURITY","PUBLIC SUPPORT","ATTENTION"};
            for(int i=0;i<6;i++)
            {
                var meter=Box("war-meter-"+i,"war-meter"); resources.Add(meter);
                var line=Box("","war-meter-heading"); meter.Add(line); var icon=Text(icons[i],"war-resource-icon"); icon.AddToClassList("war-icon-"+i); line.Add(icon); line.Add(Text(captions[i],"war-meter-caption"));
                var value=Text("","war-meter-value"); var delta=Text("","war-meter-delta"); meter.Add(value); meter.Add(delta); resourceLabels.Add((value,delta));
            }
            var turn=Box("war-turn","war-turn"); header.Add(turn);
            endTurn=Action("war-end-turn","END TURN  [T]",()=>Forward("end-turn")); endTurn.AddToClassList("war-end-turn"); turn.Add(endTurn);
            skip=Action("war-skip","SKIP DISPATCH  [SPACE]",SkipDrama); skip.AddToClassList("war-skip"); skip.style.visibility=Visibility.Hidden; turn.Add(skip);
            var dock=Box("war-dock","war-dock"); dock.pickingMode=PickingMode.Position; overlay.Add(dock);
            var reportPane=Box("war-report-pane","war-report-pane"); dock.Add(reportPane);
            var reportHead=Box("","war-report-head"); reportPane.Add(reportHead);
            officePortrait=Box("war-office-portrait","war-office-portrait"); officePortrait.pickingMode=PickingMode.Position; reportHead.Add(officePortrait);
            officeInsignia=Box("war-office-insignia","war-office-insignia");officePortrait.Add(officeInsignia);
            officeInsignia.Add(Text("1948","war-portrait-year")); officeInsignia.Add(Text("(( • ))","war-portrait-radio")); officeInsignia.Add(Text("OPERATIONS","war-portrait-caption"));
            var heading=Box("","war-report-heading"); reportHead.Add(heading);
            heading.Add(Text("INCOMING / OFFICE REPORT","war-eyebrow")); dispatchTitle=Text("Opening situation","war-report-title"); heading.Add(dispatchTitle);
            var scroll=new ScrollView {name="war-report-scroll"}; scroll.AddToClassList("war-report-scroll"); reportPane.Add(scroll);
            report=Text("","war-report-text"); scroll.Add(report);
            var commandPane=Box("war-command-pane","war-command-pane"); dock.Add(commandPane);
            var commandHead=Box("","war-command-head"); commandPane.Add(commandHead);
            commandHead.Add(Text("NATIONAL COMMAND","war-eyebrow"));
            sound=Action("war-sound","RADIO ON  [M]",ToggleSound); sound.AddToClassList("war-sound"); commandHead.Add(sound);
            var commands=Box("war-commands","war-commands"); commandPane.Add(commands);
            AddCommand(commands,"cabinet","CABINET","F1","OFFICES"); AddCommand(commands,"budget","BUDGET","F2","FINANCE");
            AddCommand(commands,"defence","DEFENCE","F3","RESERVES"); AddCommand(commands,"why","WHY?","F4","CAUSES");
            var fresh=Action("war-command-new","RESTART BRIEF\n[F5]",()=>Forward("new-game")); fresh.tooltip="Start a fresh comparison playtest from the same opening. Your regular save is not changed."; fresh.AddToClassList("war-command"); commands.Add(fresh);
            briefTitle=Text("","war-brief-title"); commandPane.Add(briefTitle);
            var goals=Box("war-goals","war-goals"); commandPane.Add(goals);
            for(int i=0;i<3;i++) { var box=Box("war-goal-"+i,"war-goal"); goals.Add(box); var title=Text("","war-goal-title"); var detail=Text("","war-goal-detail"); box.Add(title);box.Add(detail);goalLabels.Add((title,detail)); }
            briefSummary=Text("","war-brief-summary"); commandPane.Add(briefSummary);
            nextAction=Text("","war-next-action"); commandPane.Add(nextAction);
            var legend=Text("STATE-DRIVEN SYMBOLS · ILLUSTRATIVE POSITIONS & ROUTES","war-legend"); overlay.Add(legend);
            audio=new WarRoomAudio(host); visuals=new WarRoomMapVisuals(map);
            driver=host.AddComponent<WarRoomDriver>(); driver.Presentation=this;
            bridge.Changed+=Render;
            header.RegisterCallback<GeometryChangedEvent>(OnGeometry); document.RegisterCallback<GeometryChangedEvent>(OnGeometry);
            Render(); UpdateInset(); Debug.Log("WAR ROOM READY");
        }

        static VisualElement Box(string name,string css) { var b=new VisualElement {name=name,pickingMode=PickingMode.Ignore}; b.AddToClassList(css); return b; }
        static Label Text(string text,string css) { var l=new Label(text){pickingMode=PickingMode.Ignore};l.AddToClassList(css);return l; }
        Button Action(string name,string text,System.Action action)
        {
            var b=new Button(()=>{if(disposed)return;audio?.Interact();action();}){name=name,text=text}; return b;
        }
        void AddCommand(VisualElement parent,string target,string text,string hotkey,string subtitle)
        {
            var b=Action("war-command-"+target,text+"  ["+hotkey+"]\n"+subtitle,()=>Forward("nav-"+target));
            b.AddToClassList("war-command"); parent.Add(b);
        }
        void Forward(string name)
        {
            var button=document.Q<Button>(name); if(button==null || !button.enabledInHierarchy) return;
            if(name!="end-turn") SkipDrama();
            using(var e=NavigationSubmitEvent.GetPooled()) { e.target=button;button.SendEvent(e); }
        }
        void ToggleSound() { audio.Toggle(); sound.text=audio.Enabled?"RADIO ON  [M]":"RADIO OFF  [M]"; }
        void OnGeometry(GeometryChangedEvent e)=>UpdateInset();
        void UpdateInset()
        {
            if(rig==null)return; float height=document.panel?.visualTree.worldBound.height??0;
            if(height>0) {rig.TopInset=Mathf.Clamp01(96/height);rig.ApplyProjection();}
        }

        void Render()
        {
            if(disposed)return;
            if(session!=bridge.Session) { SkipDrama();session=bridge.Session;opening=bridge.State.Clone();drawnTurn=-1;drawnPlacements=-1; }
            model=WarRoomModel.From(bridge,opening);
            var minister=Cabinet.Holder(bridge.Content,bridge.State,"defense_minister");
            var portrait=portraits?.Find(minister?.Id);
            officeInsignia.style.display=portrait.HasValue?DisplayStyle.None:DisplayStyle.Flex;
            officePortrait.tooltip=portrait.HasValue?minister!.Name+" · Minister of Defence. Report text is an office summary, not a quotation.":"Office radio insignia; no portrait is available.";
            if(portrait.HasValue)
            {
                var entry=portrait.Value;officePortrait.style.backgroundImage=new StyleBackground(entry.image);
                officePortrait.style.backgroundPositionY=new BackgroundPosition(BackgroundPositionKeyword.Top,Length.Percent(PortraitFraming.PositionYPercent(entry.image.width,entry.image.height,64,62,entry.focusY)));
            }
            else officePortrait.style.backgroundImage=StyleKeyword.Null;
            date.text=model.Date+"  ·  TURN "+bridge.State.Turn;
            for(int i=0;i<model.Resources.Length;i++)
            {
                var r=model.Resources[i]; var labels=resourceLabels[i]; labels.value.text=r.Value;labels.delta.text=r.Delta;
                labels.delta.EnableInClassList("war-positive",r.Sign>0); labels.delta.EnableInClassList("war-negative",r.Sign<0);
            }
            report.text=model.Report;briefTitle.text=model.BriefTitle;briefSummary.text=model.Summary;nextAction.text=model.NextAction;
            for(int i=0;i<model.Goals.Length;i++) { var g=model.Goals[i]; goalLabels[i].title.text=(g.Met?"●  ":"○  ")+g.Text;goalLabels[i].detail.text=g.Detail;goalLabels[i].title.EnableInClassList("war-positive",g.Met);goalLabels[i].title.EnableInClassList("war-negative",!g.Met); }
            endTurn.SetEnabled(bridge.State.GameOver==null);
            if(drawnTurn==bridge.State.Turn && drawnPlacements==bridge.State.Placements.Count)return;
            bool newTurn=drawnTurn>=0 && drawnTurn!=bridge.State.Turn;
            drawnTurn=bridge.State.Turn;drawnPlacements=bridge.State.Placements.Count;
            visuals.Rebuild(model,bridge.State); RebuildTokenLabels();
            dispatchTitle.text=newTurn?"Turn "+bridge.State.Turn+" dispatch":"Opening situation";
            if(newTurn) BeginDrama();
        }

        void RebuildTokenLabels()
        {
            foreach(var label in tokenLabels)label.RemoveFromHierarchy();tokenLabels.Clear();
            foreach(var token in visuals.Tokens) {var l=Text(token.Caption,"war-token-label");mapLayer.Add(l);tokenLabels.Add(l);}
        }
        Vector3 Point(double lon,double lat) => map.Frame.ToWorld(lon,lat,0);
        void BeginDrama()
        {
            SkipDrama();audio.Dispatch();elapsed=0;drama=true;
            beforeLookAt=rig!=null?rig.LookAt:Vector3.zero;beforeDistance=rig!=null?rig.Distance:0;beforeYaw=rig!=null?rig.Yaw:0;
            AddCallout("+"+model.Arrivals.ToString("N0",CultureInfo.InvariantCulture)+" immigrants",35.03,32.845);
            AddCallout("Reserve call-up "+PlayerText.Percent(bridge.State.Mobilization),34.98,31.84);
            var treasury=model.Resources[0]; AddCallout("Treasury "+treasury.Delta,34.78,32.08);
            var mini=document.Q("minimap-image");
            if(mini!=null && map.Hexes!=null)
            {
                var b=map.Hexes.bounds;
                foreach(var coords in new[]{(35.03,32.845),(34.98,31.84),(34.73,31.03)})
                {
                    var ping=Box("","war-minimap-ping"); mini.Add(ping);
                    ping.userData=new Vector2((float)((coords.Item1-b.lon0)/(b.lon1-b.lon0)),(float)((b.lat1-coords.Item2)/(b.lat1-b.lat0)));pings.Add(ping);
                }
            }
            skip.style.visibility=Visibility.Visible;
        }
        void AddCallout(string text,double lon,double lat)
        {
            if(map==null || map.Terrain==null)return;
            var label=Text(text,"war-callout");mapLayer.Add(label);var p=Point(lon,lat);p.y=map.Terrain.SampleHeight(p)+map.Terrain.transform.position.y+1400;callouts.Add((label,p));
        }

        /// <summary>One-key interruption restores the player's pre-dispatch camera immediately.</summary>
        public void SkipDrama()
        {
            if(drama && rig!=null) {rig.LookAt=beforeLookAt;rig.Distance=beforeDistance;rig.Yaw=beforeYaw;rig.Apply();}
            drama=false;elapsed=DramaSeconds;
            foreach(var c in callouts)c.label.RemoveFromHierarchy();callouts.Clear();
            foreach(var p in pings)p.RemoveFromHierarchy();pings.Clear();
            if(skip!=null)skip.style.visibility=Visibility.Hidden;
        }

        internal void Input()
        {
            var k=Keyboard.current;if(k==null||disposed)return;
            // Text entry retains its normal keys. F-keys are otherwise independent of camera WASD/QE.
            var focused=document.panel?.focusController.focusedElement;
            if(focused is TextField || focused is IntegerField || focused is FloatField)return;
            if(k.spaceKey.wasPressedThisFrame || k.escapeKey.wasPressedThisFrame)SkipDrama();
            if(k.f1Key.wasPressedThisFrame){audio.Interact();Forward("nav-cabinet");}
            if(k.f2Key.wasPressedThisFrame){audio.Interact();Forward("nav-budget");}
            if(k.f3Key.wasPressedThisFrame){audio.Interact();Forward("nav-defence");}
            if(k.f4Key.wasPressedThisFrame){audio.Interact();Forward("nav-why");}
            if(k.f5Key.wasPressedThisFrame){audio.Interact();Forward("new-game");}
            if(k.mKey.wasPressedThisFrame){audio.Interact();ToggleSound();}
            if(k.tKey.wasPressedThisFrame){audio.Interact();Forward("end-turn");}
            if(drama && (k.wKey.isPressed||k.aKey.isPressed||k.sKey.isPressed||k.dKey.isPressed||k.qKey.isPressed||k.eKey.isPressed||
                k.upArrowKey.isPressed||k.downArrowKey.isPressed||k.leftArrowKey.isPressed||k.rightArrowKey.isPressed||
                (Mouse.current!=null && (Mouse.current.rightButton.isPressed||Mouse.current.scroll.ReadValue().sqrMagnitude>0))))SkipDrama();
        }

        internal void Tick(float delta,float time)
        {
            if(disposed)return;
            if(drama)
            {
                elapsed+=Mathf.Min(delta,.1f);
                if(elapsed>=DramaSeconds)SkipDrama();
                else if(rig!=null && map!=null && map.Terrain!=null)
                {
                    // Short, restrained flyover; no extreme zoom. The player's view returns at the end.
                    var haifa=Point(35.03,32.845);var corridor=Point(34.98,31.84);var south=Point(34.73,31.03);
                    if(elapsed<1.35f)rig.LookAt=Vector3.Lerp(beforeLookAt,haifa,Mathf.SmoothStep(0,1,elapsed/1.1f));
                    else if(elapsed<2.8f)rig.LookAt=Vector3.Lerp(haifa,corridor,Mathf.SmoothStep(0,1,(elapsed-1.35f)/1.1f));
                    else if(elapsed<3.65f)rig.LookAt=Vector3.Lerp(corridor,south,Mathf.SmoothStep(0,1,(elapsed-2.8f)/.65f));
                    else rig.LookAt=Vector3.Lerp(south,beforeLookAt,Mathf.SmoothStep(0,1,(elapsed-3.65f)/.95f));
                    rig.Distance=Mathf.Lerp(beforeDistance,Mathf.Clamp(beforeDistance*.8f,CameraController.MinDistance,85000),Mathf.Sin(elapsed/DramaSeconds*Mathf.PI)*.6f);rig.Apply();
                }
            }
            visuals.Tick(drama?elapsed/DramaSeconds:1,time,rig!=null?rig.Distance:30000);
            var mini=document.Q("minimap-image");
            for(int i=0;i<pings.Count;i++)
            {
                float p=Mathf.Repeat(elapsed*.9f+i*.3f,1);var ping=pings[i];
                if(mini!=null && mini.contentRect.width>0 && mini.contentRect.height>0)
                {
                    var uv=(Vector2)ping.userData;ping.style.left=uv.x*mini.contentRect.width;ping.style.top=uv.y*mini.contentRect.height;
                }
                ping.style.width=18+p*42;ping.style.height=18+p*42;ping.style.opacity=1-p;
            }
            var camera=rig!=null?rig.GetComponent<Camera>():null;if(camera==null||document.panel==null)return;
            bool screenOpen=document.Query<VisualElement>(className:"screen").ToList().Any(e=>e.resolvedStyle.display!=DisplayStyle.None);
            mapLayer.style.visibility=screenOpen?Visibility.Hidden:Visibility.Visible;
            for(int i=0;i<tokenLabels.Count && i<visuals.Tokens.Count;i++)
            {
                var t=visuals.Tokens[i];if(t.Go!=null)Project(tokenLabels[i],t.Go.transform.position+Vector3.up*1600,camera,0);
            }
            foreach(var c in callouts){Project(c.label,c.point,camera,-elapsed*12);c.label.style.opacity=Mathf.Min(1,(DramaSeconds-elapsed)*1.4f);}
        }
        void Project(Label label,Vector3 point,Camera camera,float rise)
        {
            var screen=camera.WorldToScreenPoint(point);
            label.style.display=screen.z>0?DisplayStyle.Flex:DisplayStyle.None;if(screen.z<=0)return;
            var panel=RuntimePanelUtils.ScreenToPanel(document.panel,new Vector2(screen.x,Screen.height-screen.y));
            label.style.left=panel.x-mapLayer.worldBound.x;label.style.top=panel.y-mapLayer.worldBound.y+rise;
        }

        public void Dispose()
        {
            if(disposed)return;disposed=true;
            bridge.Changed-=Render;header.UnregisterCallback<GeometryChangedEvent>(OnGeometry);document.UnregisterCallback<GeometryChangedEvent>(OnGeometry);
            SkipDrama();visuals.Dispose();audio.Dispose();
            driver.Presentation=null;if(Application.isPlaying)Object.Destroy(driver);else Object.DestroyImmediate(driver);
            overlay.RemoveFromHierarchy();root.RemoveFromClassList("war-room-active");if(sheet!=null)root.styleSheets.Remove(sheet);
            originalBar.style.visibility=originalVisibility;
            if(rig!=null){rig.TopInset=originalInset;rig.ApplyProjection();}
        }
    }
}
