#nullable enable
using UnityEngine;

namespace Statecraft.Game.Presentation
{
    /// <summary>Opt-in lifecycle driver; the presenter owns and destroys this component.</summary>
    public sealed class WarRoomDriver : MonoBehaviour
    {
        internal WarRoomPresentation? Presentation;
        void Update() => Presentation?.Input();
        void LateUpdate() => Presentation?.Tick(Time.unscaledDeltaTime, Time.unscaledTime);
        void OnDestroy() { var p=Presentation; Presentation=null; p?.Dispose(); }
    }
}
