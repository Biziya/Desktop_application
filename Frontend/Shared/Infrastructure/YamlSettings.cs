using System.Collections;
using System.ComponentModel;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Frontend.Shared.Infrastructure;

/// <summary>
/// Reads a section's YAML file into its typed settings class. Strict on purpose: a missing, unknown (misspelled),
/// duplicate or empty key is an error, and so is a number that is not greater than 0 or a negative margin. Every
/// error is an <see cref="InvalidDataException"/> whose message names the file and the line or key.
/// Keys are camelCase versions of the C# property names. Thickness ("8", "8,4" or "8,4,8,4"), GridLength and
/// DataGridLength values ("2.3*", "Auto", "110") are written as WPF writes them in XAML.
/// </summary>
public static class YamlSettings
{
    private static readonly IDeserializer Deserializer = new DeserializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .WithDuplicateKeyChecking()
        .WithEnforceRequiredMembers()
        .WithEnforceNullability()
        .WithTypeConverter(new WpfValueConverter())
        .Build();

    private static readonly NullabilityInfoContext Nullability = new();

    /// <param name="relativePath">Path below the application folder, e.g. "Pages/Measurement/Table/MeasurementTable.yaml".</param>
    public static T Load<T>(string baseDirectory, string relativePath) where T : class
    {
        var path = Path.Combine(baseDirectory, relativePath);

        if (!File.Exists(path))
        {
            throw new InvalidDataException($"{relativePath}: configuration file not found ({path}).");
        }

        var text = File.ReadAllText(path);
        T? settings;

        try
        {
            settings = Deserializer.Deserialize<T>(text);
        }
        catch (YamlException error)
        {
            var line = (int)error.Start.Line;
            var source = text.Split('\n').ElementAtOrDefault(line - 1)?.Trim();
            var reason = error.InnerException?.Message ?? error.Message;
            throw new InvalidDataException(
                $"{relativePath}, line {line}{(string.IsNullOrEmpty(source) ? "" : $" (\"{source}\")")}: {reason}", error);
        }

        if (settings is null)
        {
            throw new InvalidDataException($"{relativePath}: the file is empty.");
        }

        CheckValues(settings, "", relativePath);
        return settings;
    }

    /// <summary>An error in <paramref name="file"/> at <paramref name="key"/> (e.g. "columns[3].alignment").</summary>
    public static InvalidDataException Invalid(string file, string key, string message) => new($"{file}: {key}: {message}");

    // Sizes must be greater than 0 and margins not negative; nested settings and lists are checked the same way.
    private static void CheckValues(object settings, string path, string file)
    {
        foreach (var property in settings.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            var key = path + char.ToLowerInvariant(property.Name[0]) + property.Name[1..];
            var value = property.GetValue(settings);

            switch (value)
            {
                case null when Nullability.Create(property).ReadState != NullabilityState.Nullable:
                    throw Invalid(file, key, "a value is required.");
                case double number when !double.IsFinite(number) || number <= 0:
                    throw Invalid(file, key, $"must be a number greater than 0 (is {number}).");
                case Thickness t when !(t.Left >= 0 && t.Top >= 0 && t.Right >= 0 && t.Bottom >= 0):
                    throw Invalid(file, key, $"margins and paddings cannot be negative (is {t}).");
                case GridLength { IsAuto: false } length when length.Value <= 0:
                    throw Invalid(file, key, $"must be Auto, a star share or pixels greater than 0 (is {length}).");
                case DataGridLength { IsAuto: false } length when length.Value <= 0:
                    throw Invalid(file, key, $"must be Auto, a star share or pixels greater than 0 (is {length}).");
                case IList list:
                    for (var i = 0; i < list.Count; i++)
                    {
                        CheckValues(list[i]!, $"{key}[{i}].", file);
                    }

                    break;
                case not null when property.PropertyType.IsClass && property.PropertyType != typeof(string):
                    CheckValues(value, key + ".", file);
                    break;
            }
        }
    }

    // Thickness, GridLength and DataGridLength from their XAML text, with the line of a value that is not valid.
    private sealed class WpfValueConverter : IYamlTypeConverter
    {
        public bool Accepts(Type type) => type == typeof(Thickness) || type == typeof(GridLength) || type == typeof(DataGridLength);

        public object? ReadYaml(IParser parser, Type type, ObjectDeserializer rootDeserializer)
        {
            var scalar = parser.Consume<Scalar>();

            try
            {
                return TypeDescriptor.GetConverter(type).ConvertFromInvariantString(scalar.Value);
            }
            catch (Exception error) when (error is FormatException or NotSupportedException or ArgumentException or InvalidCastException)
            {
                throw new YamlException(scalar.Start, scalar.End, $"'{scalar.Value}' is not a valid {type.Name}.");
            }
        }

        public void WriteYaml(IEmitter emitter, object? value, Type type, ObjectSerializer serializer) => throw new NotSupportedException();
    }
}
