// SPDX-License-Identifier: GPL-3.0-only
// Additional terms: see ADDITIONAL_TERMS.md in the repository root.
using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Wonder.Extensibility;

namespace Wonder.Desktop;

internal sealed class PluginConfigurationForm : StackPanel
{
    private readonly PluginConfigurationSchema _schema;
    private readonly JsonObject _document;
    private readonly List<(PluginConfigurationField Field, Func<JsonNode?> Read)> _inputs = [];

    internal PluginConfigurationForm(PluginConfigurationSchema schema, string json)
    {
        PluginConfiguration.Validate(schema, json);
        _schema = schema;
        _document = JsonNode.Parse(json)!.AsObject();
        Spacing = 16;
        foreach (var field in schema.Fields)
        {
            var group = new StackPanel { Spacing = 6 };
            group.Children.Add(new TextBlock { Text = field.Label, FontWeight = FontWeight.SemiBold, Foreground = Palette.Text });
            if (!string.IsNullOrEmpty(field.Description))
                group.Children.Add(new TextBlock { Text = field.Description, Foreground = Palette.Muted, FontSize = 12, TextWrapping = TextWrapping.Wrap });
            Control editor;
            Func<JsonNode?> read;
            if (field.Type == "boolean")
            {
                var input = new CheckBox { Content = "启用", IsChecked = _document[field.Key]!.GetValue<bool>() };
                editor = input;
                read = () => JsonValue.Create(input.IsChecked == true);
            }
            else if (field.Type == "choice")
            {
                var input = new ComboBox { ItemsSource = field.Options, SelectedItem = _document[field.Key]!.GetValue<string>(), HorizontalAlignment = HorizontalAlignment.Stretch };
                editor = input;
                read = () => JsonValue.Create(input.SelectedItem as string);
            }
            else
            {
                var input = new TextBox
                {
                    Text = field.Type == "string" ? _document[field.Key]!.GetValue<string>() : _document[field.Key]!.ToJsonString(),
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    MaxLength = field.Type == "string" ? field.MaxLength ?? 65536 : 128
                };
                editor = input;
                read = () => field.Type == "string" ? JsonValue.Create(input.Text ?? "") : ReadNumber(field, input.Text);
                if (field.Type is "number" or "integer")
                {
                    var kind = field.Type == "integer" ? "整数" : "数字（小数点使用 .）";
                    group.Children.Add(new TextBlock { Text = kind +
                        (field.Minimum is { } min ? " · 最小 " + min.ToString(CultureInfo.InvariantCulture) : "") +
                        (field.Maximum is { } max ? " · 最大 " + max.ToString(CultureInfo.InvariantCulture) : ""),
                        FontSize = 11, Foreground = Palette.Muted });
                }
            }
            AutomationProperties.SetName(editor, field.Label);
            AutomationProperties.SetHelpText(editor, field.Description);
            group.Children.Add(editor);
            Children.Add(group);
            _inputs.Add((field, read));
        }
    }

    private static JsonNode ReadNumber(PluginConfigurationField field, string? text)
    {
        if (!decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
            throw new InvalidDataException(field.Label + "必须填写有效数字。");
        return JsonValue.Create(number)!;
    }

    internal string Serialize()
    {
        var result = _document.DeepClone().AsObject();
        foreach (var (field, read) in _inputs) result[field.Key] = read();
        var json = result.ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
        PluginConfiguration.Validate(_schema, json);
        return json + "\n";
    }
}
