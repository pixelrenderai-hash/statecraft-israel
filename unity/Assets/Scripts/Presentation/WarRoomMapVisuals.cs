#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Statecraft.Game.Map;
using Statecraft.Sim;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace Statecraft.Game.Presentation
{
    /// <summary>Original low-poly operational symbols. Counts follow reserve call-up, security, arrival causes and
    /// placements; routes and positions are illustrative, not claims about a historical formation's movements.</summary>
    internal sealed class WarRoomMapVisuals : IDisposable
    {
        internal sealed class Token
        {
            internal GameObject Go = null!; internal Vector3 From, To; internal string Caption = "";
            internal bool Ship, Moving; internal float Phase;
        }
        readonly MapScene map; readonly GameObject? holder; readonly List<Token> tokens = new List<Token>();
        const float ShipHeightM = 1050f;
        readonly Mesh? cube, hull; readonly Material? olive, brass, cream, cyan;
        internal IReadOnlyList<Token> Tokens => tokens;

        internal WarRoomMapVisuals(MapScene map)
        {
            this.map = map;
            if (map == null || map.Terrain == null) return;
            holder = new GameObject("WarRoomOperationalSymbols"); holder.transform.SetParent(map.transform, false);
            var own = holder.AddComponent<MapRuntimeAssets>();
            cube = own.Own(BoxMesh()); hull = own.Own(HullMesh());
            olive = own.Own(Material("Reserve olive", new Color(.20f, .27f, .19f)));
            brass = own.Own(Material("Signal brass", new Color(.86f, .66f, .32f)));
            cream = own.Own(Material("Warm canvas", new Color(.83f, .81f, .64f)));
            cyan = own.Own(Material("Radio teal", new Color(.25f, .72f, .65f)));
        }

        Material Material(string name, Color colour)
        {
            // Clone a scene-referenced material so its shader is retained by the WebGL build.
            var m = new Material(map.MarkerMaterial) { name = "WarRoom " + name };
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", colour);
            if (m.HasProperty("_Color")) m.SetColor("_Color", colour);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", .1f);
            return m;
        }

        static Mesh BoxMesh()
        {
            var mesh = new Mesh { name = "WarRoom original box" };
            mesh.vertices = new[] { new Vector3(-.5f,0,-.5f), new Vector3(.5f,0,-.5f), new Vector3(.5f,1,-.5f), new Vector3(-.5f,1,-.5f), new Vector3(-.5f,0,.5f), new Vector3(.5f,0,.5f), new Vector3(.5f,1,.5f), new Vector3(-.5f,1,.5f) };
            mesh.triangles = new[] {0,2,1,0,3,2, 4,5,6,4,6,7, 0,1,5,0,5,4, 3,7,6,3,6,2, 0,4,7,0,7,3, 1,2,6,1,6,5};
            mesh.RecalculateNormals(); mesh.RecalculateBounds(); return mesh;
        }

        static Mesh HullMesh()
        {
            var mesh = new Mesh { name = "WarRoom original steamer hull" };
            mesh.vertices = new[] {new Vector3(-.4f,0,-.5f),new Vector3(.4f,0,-.5f),new Vector3(.4f,.3f,.28f),new Vector3(0,.3f,.6f),new Vector3(-.4f,.3f,.28f),new Vector3(-.4f,.3f,-.5f),new Vector3(.4f,.3f,-.5f)};
            mesh.triangles = new[] {0,6,1,0,5,6, 1,6,2,1,2,3, 0,4,5,0,3,4, 5,4,3,5,3,2,5,2,6, 0,1,3};
            mesh.RecalculateNormals(); mesh.RecalculateBounds(); return mesh;
        }

        void Part(Transform parent, string name, Vector3 position, Vector3 scale, Material material, Mesh? mesh = null)
        {
            var go = new GameObject(name); go.transform.SetParent(parent, false); go.transform.localPosition = position; go.transform.localScale = scale;
            go.AddComponent<MeshFilter>().sharedMesh = mesh ?? cube;
            var r = go.AddComponent<MeshRenderer>(); r.sharedMaterial = material; r.shadowCastingMode = ShadowCastingMode.Off;
        }

        Vector3 Ground(double lon, double lat, bool ship = false)
        {
            var p = map.Frame.ToWorld(lon, lat, 0);
            p.y = ship ? ShipRest(p, 1) : map.Terrain.SampleHeight(p) + map.Terrain.transform.position.y + 45;
            return p;
        }
        float ShipRest(Vector3 point, float scale) => PreviewProps.RestY(map.Terrain, PreviewProps.Kind.Sea,
            new Bounds(point + Vector3.up * ShipHeightM * scale * .5f, new Vector3(1200, ShipHeightM, 1250) * scale));
        Settlement? Place(string id) => map.Settlements.FirstOrDefault(s => s.Id == id);
        Vector3 Near(Settlement? s, double lon, double lat, double east = 0, double north = 0) => Ground((s?.Lon ?? lon) + east, (s?.Lat ?? lat) + north);

        Token Add(string caption, Vector3 from, Vector3 to, bool moving, bool ship, int index)
        {
            var token = new Token { Go = new GameObject("WarRoom " + caption), From = from, To = to, Caption = caption, Moving = moving, Ship = ship, Phase = index * .17f };
            token.Go.transform.SetParent(holder!.transform, false); token.Go.transform.position = to;
            tokens.Add(token);
            var ring = new GameObject("Signal ring"); ring.transform.SetParent(token.Go.transform, false);
            var line = ring.AddComponent<LineRenderer>(); line.useWorldSpace = false; line.loop = true; line.positionCount = 32; line.widthMultiplier = 45; line.sharedMaterial = ship ? cyan : brass;
            line.shadowCastingMode = ShadowCastingMode.Off;
            for (int j = 0; j < 32; j++) { var a = j * Mathf.PI * 2 / 32; line.SetPosition(j, new Vector3(Mathf.Cos(a)*620, ship ? 150 : 100, Mathf.Sin(a)*620)); }
            return token;
        }

        internal void Rebuild(WarRoomModel model, GameState state)
        {
            if (holder == null) return;
            foreach (var t in tokens) Destroy(t.Go); tokens.Clear();
            string[] bases = {"tel_aviv", "jerusalem", "haifa", "rehovot", "degania_alef", "revivim"};
            for (int i = 0; i < model.BrigadeSymbols; i++)
            {
                var p = Near(Place(bases[i]), 34.9, 31.9, .018, -.012);
                var t = Add("RESERVE", p, p, false, false, i); var parent = t.Go.transform;
                Part(parent, "Base", new Vector3(0,90,0), new Vector3(700,120,600), olive!);
                for (int j = -1; j <= 1; j++)
                {
                    Part(parent, "Figure", new Vector3(j*230,210,0), new Vector3(125,280,150), cream!);
                    Part(parent, "Helmet", new Vector3(j*230,490,0), new Vector3(160,95,170), olive!);
                }
                Part(parent, "Standard", new Vector3(440,110,0), new Vector3(35,760,35), brass!);
                Part(parent, "Pennant", new Vector3(590,650,0), new Vector3(280,180,30), cyan!);
            }
            for (int i = 0; i < model.ShipSymbols; i++)
            {
                var t = Add("ARRIVALS", Ground(35.025 + i*.025,32.94,true), Ground(35.03+i*.018,32.845+i*.015,true), true, true, i);
                var p = t.Go.transform;
                Part(p,"Hull",Vector3.zero,new Vector3(700,650,1250),olive!,hull);
                Part(p,"Deckhouse",new Vector3(0,200,-100),new Vector3(390,240,520),cream!);
                Part(p,"Funnel",new Vector3(0,440,-190),new Vector3(140,350,160),brass!);
                Part(p,"Mast",new Vector3(0,200,330),new Vector3(28,850,28),cream!);
            }
            var south = map.Settlements.Where(s => s.Lat < 31.35).OrderBy(s => s.Id, StringComparer.Ordinal).ToArray();
            for (int i = 0; i < model.ConvoySymbols; i++)
            {
                var dest = south.Length > 0 ? south[i % south.Length] : Place("rehovot");
                var t = Add("SUPPLY", Near(Place("tel_aviv"),34.78,32.08,.02,-.02), Near(dest,34.73,31.03,.018,.015), true, false, i);
                Trucks(t.Go.transform, olive!);
            }
            int k = 0;
            foreach (var placement in state.Placements.Where(p => p.Action == MapActions.TransitCamp || p.Action == MapActions.Housing).Take(model.CampTruckSymbols))
            {
                var h = map.Hexes?.hexes.FirstOrDefault(x => x.q == placement.Q && x.r == placement.R); if (h == null) continue;
                var p = Ground(h.lon + .012, h.lat);
                var t = Add("ABSORPTION", Near(Place("haifa"),35,32.8,.02,.02), p, true, false, k++);
                Trucks(t.Go.transform, cream!);
            }
            Tick(1, 0, 30000);
        }

        void Trucks(Transform parent, Material material)
        {
            for (int j = 0; j < 2; j++)
            {
                var offset = new Vector3(j*530-265,120,0);
                Part(parent,"Canvas truck",offset,new Vector3(320,240,570),material);
                Part(parent,"Cab",offset+new Vector3(0,-30,370),new Vector3(300,190,200),brass!);
                for (int x=-1; x<=1; x+=2)
                    for(int z=-1;z<=1;z+=2) Part(parent,"Wheel",offset+new Vector3(x*170,-80,z*210),new Vector3(70,100,120),olive!);
            }
        }

        internal void Tick(float turnProgress, float time, float cameraDistance)
        {
            float scale = Mathf.Clamp(cameraDistance / 38000, .8f, 2.6f);
            foreach (var t in tokens)
            {
                if (t.Go == null) continue;
                float p = t.Moving ? Mathf.SmoothStep(0,1,Mathf.Clamp01(turnProgress*1.2f-t.Phase*.2f)) : 1;
                var pos = Vector3.Lerp(t.From,t.To,p);
                if (!t.Ship) pos.y = map.Terrain.SampleHeight(pos)+map.Terrain.transform.position.y+45;
                else pos.y = ShipRest(pos, scale) + Mathf.Sin(time*1.3f+t.Phase*6)*20;
                t.Go.transform.position = pos; t.Go.transform.localScale = Vector3.one*scale;
                if (t.Moving) { var direction = t.To-t.From; direction.y=0; if(direction.sqrMagnitude>1) t.Go.transform.rotation=Quaternion.LookRotation(direction); }
            }
        }

        static void Destroy(Object o) { if (Application.isPlaying) Object.Destroy(o); else Object.DestroyImmediate(o); }
        public void Dispose() { tokens.Clear(); if(holder != null) Destroy(holder); }
    }
}
