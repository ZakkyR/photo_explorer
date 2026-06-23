using System.Globalization;
using System.IO;
using System.Windows.Data;

namespace PhotoExplorer.App.Converters;

public class PathToNameConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is string path ? Path.GetFileName(path.TrimEnd('\\', '/')) : value;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
