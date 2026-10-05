namespace Takupoke.Core.Recovery;
public sealed record RecoveryBox(double X, double Y, double Width, double Height)
{
    public bool Valid => new[] { X, Y, Width, Height, X + Width, Y + Height }.All(double.IsFinite) && X >= 0 && Y >= 0 && Width > 0 && Height > 0;
    public bool Contains(RecoveryBox other) => Valid && other.Valid && other.X >= X && other.Y >= Y && other.X + other.Width <= X + Width && other.Y + other.Height <= Y + Height;
}
