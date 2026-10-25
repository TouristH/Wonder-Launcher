// SPDX-License-Identifier: GPL-3.0-only
// Additional terms: see ADDITIONAL_TERMS.md in the repository root.

using System.Text.Json;
using static Wonder.Infrastructure.LocalFiles;

namespace Wonder.Infrastructure;

internal static class LoggingConfiguration
{
    public static string? Argument(JsonElement logging, string assets, string cache, List<string> notices)
    {
        var argument = logging.Get("argument").Text();
        if (argument is null) return null;
        var id = logging.Get("file").Get("id").Text();
        if (id is not null)
        {
            var source = Under(Path.Combine(assets, "log_configs"), Segment(id));
            if (File.Exists(source)) return argument.Replace("${path}", source);
        }
        // Third-party launchers do not necessarily cache Mojang's optional logging XML.
        // Keep game dependencies strict; supply our own console logger in our own cache.
        if (logging.Get("type").Text() == "log4j2-xml" || argument == "-Dlog4j.configurationFile=${path}")
        {
            var directory = Path.Combine(Path.GetFullPath(cache), "log_configs"); Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, "wonder-console-v1.xml");
            const string configuration = """
                <?xml version="1.0" encoding="UTF-8"?>
                <Configuration status="WARN">
                  <Appenders>
                    <Console name="WonderConsole" target="SYSTEM_OUT">
                      <PatternLayout charset="UTF-8" pattern="[%d{HH:mm:ss}] [%t/%level]: %msg{nolookups}%n"/>
                    </Console>
                  </Appenders>
                  <Loggers><Root level="info"><AppenderRef ref="WonderConsole"/></Root></Loggers>
                </Configuration>
                """;
            File.WriteAllText(path, configuration);
            notices.Add("原启动器未缓存可选日志配置，已使用 Wonder 的本地控制台日志配置。");
            return argument.Replace("${path}", path);
        }
        notices.Add("可选日志配置不可用，使用游戏自带的日志设置。");
        return null;
    }
}
