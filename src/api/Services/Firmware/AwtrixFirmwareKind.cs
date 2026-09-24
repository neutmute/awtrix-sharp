namespace AwtrixSharpWeb.Services.Firmware
{
    /// <summary>
    /// Which firmware a device runs. Selects the addressing and payload dialect (see AwtrixFirmware.For).
    /// Awtrix3 is the default and is transitional; NG is https://blueforcer.github.io/awtrix-ng/.
    /// </summary>
    public enum AwtrixFirmwareKind
    {
        Awtrix3 = 0,
        NG = 1,
    }
}
