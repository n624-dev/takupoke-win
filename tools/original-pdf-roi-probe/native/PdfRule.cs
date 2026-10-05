namespace Takupoke.Infrastructure.Parsing;
public sealed record PdfRule(double X1, double Y1, double X2, double Y2)
{ public bool Vertical => Math.Abs(X1 - X2) < 0.2; public bool Horizontal => Math.Abs(Y1 - Y2) < 0.2; }
