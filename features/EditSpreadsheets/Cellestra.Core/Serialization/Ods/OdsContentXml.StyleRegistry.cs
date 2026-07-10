using System.Globalization;
using System.Xml.Linq;
using Cellestra.Core.Serialization.Ods.Mapping;
using Cellestra.Core.Styling;
using static Cellestra.Core.Serialization.Ods.OdsXmlNames;

namespace Cellestra.Core.Serialization.Ods;

public static partial class OdsContentXml
{
    // ---- Style registry (write side) -------------------------------------

    private sealed class StyleRegistry
    {
        private readonly XElement _autoStyles;
        private readonly Dictionary<int, string> _cellStyleNames = [];
        private readonly Dictionary<string, string> _dataStyleNames = new(StringComparer.Ordinal);
        private readonly Dictionary<double, string> _columnStyleNames = [];
        private readonly Dictionary<double, string> _rowStyleNames = [];
        private int _next;

        public StyleRegistry(StylePool styles, XElement autoStyles)
        {
            Styles = styles;
            _autoStyles = autoStyles;
        }

        public StylePool Styles { get; }

        public string CellStyle(int styleId)
        {
            if (_cellStyleNames.TryGetValue(styleId, out var existing))
            {
                return existing;
            }

            var style = Styles.Get(styleId);
            string? dataStyleName = null;
            if (!style.NumberFormat.IsGeneral)
            {
                dataStyleName = DataStyle(style.NumberFormat);
            }

            string name = $"ce{_next++}";
            _autoStyles.Add(OdsCellStyles.Build(name, style, dataStyleName));
            _cellStyleNames[styleId] = name;
            return name;
        }

        private string DataStyle(NumberFormat format)
        {
            if (_dataStyleNames.TryGetValue(format.Code, out var existing))
            {
                return existing;
            }

            string name = $"N{_next++}";
            var element = OdsDataStyles.Build(name, format);
            if (element is null)
            {
                return name; // General; should not happen since caller checks
            }

            _autoStyles.Add(element);
            _dataStyleNames[format.Code] = name;
            return name;
        }

        public string ColumnStyle(double width)
        {
            if (_columnStyleNames.TryGetValue(width, out var existing))
            {
                return existing;
            }

            string name = $"co{_next++}";
            _autoStyles.Add(new XElement(Style + "style",
                new XAttribute(Style + "name", name),
                new XAttribute(Style + "family", "table-column"),
                new XElement(Style + "table-column-properties",
                    new XAttribute(Style + "column-width", FormatCm(PxToCm(width))))));
            _columnStyleNames[width] = name;
            return name;
        }

        public string RowStyle(double height)
        {
            if (_rowStyleNames.TryGetValue(height, out var existing))
            {
                return existing;
            }

            string name = $"ro{_next++}";
            _autoStyles.Add(new XElement(Style + "style",
                new XAttribute(Style + "name", name),
                new XAttribute(Style + "family", "table-row"),
                new XElement(Style + "table-row-properties",
                    new XAttribute(Style + "row-height", FormatCm(PxToCm(height))))));
            _rowStyleNames[height] = name;
            return name;
        }

        private static string FormatCm(double cm) => cm.ToString("0.###", CultureInfo.InvariantCulture) + "cm";
    }
}
