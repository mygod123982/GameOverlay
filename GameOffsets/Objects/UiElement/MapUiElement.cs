namespace GameOffsets.Objects.UiElement
{
    using System.Runtime.InteropServices;
    using GameOffsets.Natives;

    [StructLayout(LayoutKind.Explicit, Pack = 1)]
    public struct MapUiElementOffset
    {
        [FieldOffset(0x000)] public UiElementBaseOffset UiElementBase;
        [FieldOffset(0x298)] public StdTuple2D<float> Shift;
        [FieldOffset(0x2A0)] public StdTuple2D<float> DefaultShift;
        [FieldOffset(0x2DC)] public float Zoom;
    }
}
