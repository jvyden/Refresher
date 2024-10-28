﻿using Refresher.Core.Pipelines.Steps;

namespace Refresher.Core.Pipelines;

public class PS3PatchPipeline : Pipeline
{
    public override string Id => "ps3-patch";
    public override string Name => "PS3 Patch";

    protected override List<Type> StepTypes =>
    [
        // Info gathering stage
        typeof(SetupPS3AccessorStep),
        typeof(ValidateGameStep),
        typeof(DownloadParamSfoStep),
        typeof(DownloadGameEbootStep),
        typeof(ReadEbootContentIdStep),
        typeof(DownloadGameLicenseStep),
        
        // Decryption and patch stage
        typeof(PrepareSceToolStep),
        typeof(DecryptGameEbootStep),
        typeof(PrepareEbootPatcherAndVerifyStep),
        typeof(ApplyPatchToEbootStep),
        
        // Encryption and upload stage
        typeof(EncryptGameEbootStep),
        typeof(BackupGameEbootBeforeReplaceStep),
        typeof(UploadGameEbootStep),
    ];
}