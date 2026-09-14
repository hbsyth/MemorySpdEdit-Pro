namespace SpdEditor.Core.Xmp;

public static class XmpProfileService
{
    public static bool ExportProfile(IWin32Window owner, byte[] profileBytes, string defaultName)
    {
        using var dlg = new SaveFileDialog
        {
            Title = "导出 XMP/EXPO Profile",
            Filter = "Profile (*.bin)|*.bin|所有文件|*.*",
            FileName = defaultName,
            DefaultExt = "bin",
        };
        if (dlg.ShowDialog(owner) != DialogResult.OK) return false;
        File.WriteAllBytes(dlg.FileName, profileBytes);
        MessageBox.Show(owner, $"已导出到:\n{dlg.FileName}", "导出成功", MessageBoxButtons.OK, MessageBoxIcon.Information);
        return true;
    }

    public static byte[]? ImportProfile(IWin32Window owner, int expectedSize)
    {
        using var dlg = new OpenFileDialog
        {
            Title = "导入 XMP/EXPO Profile",
            Filter = "Profile (*.bin)|*.bin|所有文件|*.*",
        };
        if (dlg.ShowDialog(owner) != DialogResult.OK) return null;
        var data = File.ReadAllBytes(dlg.FileName);
        if (data.Length != expectedSize)
        {
            MessageBox.Show(owner, $"文件大小必须为 {expectedSize} 字节，实际为 {data.Length} 字节。", "导入失败",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            return null;
        }
        return data;
    }

    public static bool ValidateDdr5Xmp(Ddr5XmpProfile profile) =>
        !profile.IsEmpty && profile.CheckCrcValidity();

    public static bool ValidateDdr4Xmp(Ddr4XmpProfile profile) => !profile.IsEmpty;
}
