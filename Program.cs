namespace SpdEditor;

/// <summary>应用程序入口。</summary>
static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        try
        {
            AppPaths.Initialize(args);
            ApplicationConfiguration.Initialize();
            // 背景/系统色跟随 Windows 浅色·深色设定（Win11+；启动时生效）
#pragma warning disable WFO5001
            Application.SetColorMode(SystemColorMode.System);
#pragma warning restore WFO5001
            Application.Run(new MainForm());
        }
        catch (Exception ex)
        {
            try
            {
                MessageBox.Show(
                    $"程序启动失败:\n{ex.GetType().Name}: {ex.Message}\n\n{ex.StackTrace}",
                    "SpdEditor",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
            catch
            {
                // ignore
            }
        }
    }
}
