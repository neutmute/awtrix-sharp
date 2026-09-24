namespace AwtrixSharpWeb.Services.Firmware
{
    /// <summary>Static registry of firmware profiles; they are stateless so one instance each is enough.</summary>
    public static class AwtrixFirmware
    {
        private static readonly IAwtrixFirmware Awtrix3 = new Awtrix3Firmware();

        public static IAwtrixFirmware For(AwtrixFirmwareKind kind)
        {
            return kind switch
            {
                AwtrixFirmwareKind.Awtrix3 => Awtrix3,
                _ => throw new NotSupportedException($"Firmware '{kind}' is not supported"),
            };
        }
    }
}
