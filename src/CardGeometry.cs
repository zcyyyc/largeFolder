using System;

namespace LargeFolder
{
    public struct CardBounds
    {
        public double Left;
        public double Top;
        public double Width;
        public double Height;
    }

    public struct CardPreviewLayout
    {
        public int Columns;
        public int Rows;
        public int Capacity;
        public bool HasMore;
    }

    /// <summary>Pure DIP geometry for a resize measured from the pointer-down position.</summary>
    public static class CardGeometry
    {
        public const double MinCompactWidth = 142;
        public const double MinCompactHeight = 112;
        public const double MinExpandedWidth = 350;
        public const double MinExpandedHeight = 170;
        public const double MaxWidth = 1200;
        public const double MaxHeight = 1000;

        public static CardPreviewLayout Preview(double width, double height, int count)
        {
            width = Finite(width) && width > 0 ? width : MinCompactWidth;
            height = Finite(height) && height > 0 ? height : MinCompactHeight;
            count = Math.Max(0, count);
            int columns = CellCount(width - 30, 2);
            int rows = CellCount(height - 56, 1);
            int capacity = Capacity(columns, rows);
            if (count > capacity)
            {
                rows = CellCount(height - 80, 1);
                capacity = Capacity(columns, rows);
            }
            return new CardPreviewLayout
            {
                Columns = columns,
                Rows = rows,
                Capacity = capacity,
                HasMore = count > capacity
            };
        }

        public static CardBounds Resize(double left, double top, double width, double height,
            double horizontal, double vertical, bool leftEdge, bool topEdge, bool rightEdge, bool bottomEdge,
            double minWidth, double minHeight, double maxWidth, double maxHeight)
        {
            left = Finite(left) ? left : 0;
            top = Finite(top) ? top : 0;
            minWidth = ValidMinimum(minWidth);
            minHeight = ValidMinimum(minHeight);
            maxWidth = ValidMaximum(maxWidth, minWidth);
            maxHeight = ValidMaximum(maxHeight, minHeight);
            width = Finite(width) && width > 0 ? width : minWidth;
            height = Finite(height) && height > 0 ? height : minHeight;
            horizontal = Finite(horizontal) ? horizontal : 0;
            vertical = Finite(vertical) ? vertical : 0;

            // Opposite edges cannot both be dragged by a real grip. Prefer left/top if a caller sets both.
            double resizedWidth = Clamp(width + (leftEdge ? -horizontal : (rightEdge ? horizontal : 0)), minWidth, maxWidth);
            double resizedHeight = Clamp(height + (topEdge ? -vertical : (bottomEdge ? vertical : 0)), minHeight, maxHeight);
            return new CardBounds
            {
                Left = leftEdge ? FiniteCoordinate(left + (width - resizedWidth), left) : left,
                Top = topEdge ? FiniteCoordinate(top + (height - resizedHeight), top) : top,
                Width = resizedWidth,
                Height = resizedHeight
            };
        }

        private static bool Finite(double value)
        {
            return !Double.IsNaN(value) && !Double.IsInfinity(value);
        }

        private static int CellCount(double available, int minimum)
        {
            double cells = Math.Floor(available / 56);
            if (cells >= Int32.MaxValue) return Int32.MaxValue;
            return cells <= minimum ? minimum : (int)cells;
        }

        private static int Capacity(int columns, int rows)
        {
            long capacity = (long)columns * rows;
            return capacity > Int32.MaxValue ? Int32.MaxValue : (int)capacity;
        }

        private static double ValidMinimum(double value)
        {
            return Finite(value) && value > 0 ? value : 1;
        }

        private static double ValidMaximum(double value, double minimum)
        {
            return Finite(value) && value >= minimum ? value : minimum;
        }

        private static double Clamp(double value, double minimum, double maximum)
        {
            if (Double.IsNaN(value)) return minimum;
            return Math.Max(minimum, Math.Min(maximum, value));
        }

        private static double FiniteCoordinate(double value, double fallback)
        {
            return Finite(value) ? value : fallback;
        }
    }
}
