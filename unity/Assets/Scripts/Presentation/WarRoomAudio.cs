#nullable enable
using System;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Statecraft.Game.Presentation
{
    /// <summary>Original, deterministic synthesis: relay clicks, a radio signal and a very quiet sea/radio bed.
    /// No recordings, historical speeches or third-party music. Sound begins on the player's first interaction.</summary>
    internal sealed class WarRoomAudio : IDisposable
    {
        readonly GameObject? holder; readonly AudioSource? ambient, effects;
        readonly AudioClip? click, signal, bed;
        internal bool Enabled { get; private set; } = true;
        bool started;

        internal WarRoomAudio(GameObject host)
        {
            if (!Application.isPlaying) return;
            holder = new GameObject("WarRoomOriginalAudio"); holder.transform.SetParent(host.transform, false);
            ambient = holder.AddComponent<AudioSource>(); effects = holder.AddComponent<AudioSource>();
            ambient.playOnAwake = effects.playOnAwake = false; ambient.loop = true;
            ambient.spatialBlend = effects.spatialBlend = 0; ambient.volume = .14f; effects.volume = .25f;
            click = Synth("WarRoom relay click", .09f, 0);
            signal = Synth("WarRoom radio dispatch", .65f, 1);
            bed = Synth("WarRoom original radio and sea", 8, 2); ambient.clip = bed;
        }

        static AudioClip Synth(string name, float seconds, int kind)
        {
            const int rate = 22050; int n = (int)(rate*seconds); var samples = new float[n];
            uint rng = 1948; float filtered = 0;
            float[] notes={220,261.6256f,329.6276f,293.6648f};
            for (int i=0; i<n; i++)
            {
                float t=(float)i/rate; rng=1664525*rng+1013904223; float noise=(rng & 65535)/32768f-1;
                filtered += .025f*(noise-filtered); float value;
                if(kind==0) value=(noise*.35f+Mathf.Sin(t*2*Mathf.PI*760)*.20f)*Mathf.Exp(-t*70);
                else if(kind==1)
                {
                    float gate = (t<.10f || (t>.17f && t<.27f) || (t>.35f && t<.56f)) ? 1 : 0;
                    value = Mathf.Sin(t*2*Mathf.PI*660)*.15f*gate;
                }
                else
                {
                    float swell=.5f+.5f*Mathf.Sin(t*2*Mathf.PI/8);
                    value=filtered*.32f*swell+Mathf.Sin(t*2*Mathf.PI*110)*.008f;
                    // A sparse, original four-note radio motif, not a historical composition.
                    int note=(int)(t/2);
                    value+=Mathf.Sin(t*2*Mathf.PI*notes[Math.Min(3,note)])*.014f*Mathf.Exp(-(t%2)*3);
                }
                float edge=Mathf.Min(1,Mathf.Min(t/.006f,(seconds-t)/.012f)); samples[i]=value*edge;
            }
            var clip=AudioClip.Create(name,n,1,rate,false); clip.SetData(samples,0); return clip;
        }
        internal void Interact()
        {
            if(!Enabled || holder==null) return;
            if(!started) { started=true; ambient!.Play(); }
            effects!.PlayOneShot(click);
        }
        internal void Dispatch() { if(Enabled && started && effects!=null) effects.PlayOneShot(signal); }
        internal void Toggle()
        {
            Enabled=!Enabled;
            if(ambient!=null) { if(Enabled && started) ambient.Play(); else ambient.Stop(); }
        }
        static void Destroy(Object? o) { if(o==null) return; if(Application.isPlaying) Object.Destroy(o); else Object.DestroyImmediate(o); }
        public void Dispose() { if(ambient!=null) ambient.Stop(); if(effects!=null) effects.Stop(); Destroy(holder); Destroy(click); Destroy(signal); Destroy(bed); }
    }
}
