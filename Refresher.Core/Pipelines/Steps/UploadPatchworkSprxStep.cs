﻿using System.Reflection;
using Refresher.Core.Accessors;

namespace Refresher.Core.Pipelines.Steps;

public class UploadPatchworkSprxStep : Step
{
    public UploadPatchworkSprxStep(Pipeline pipeline) : base(pipeline)
    {}

    public override float Progress { get; protected set; }
    public override async Task ExecuteAsync(CancellationToken cancellationToken = default)
    {
        await PatchAccessor.TryAsync(async () =>
        {
            const string pluginsFolder = "plugins/";
            const string sprxName = "patchwork.sprx";
            const string sprxNameEmulator = "patchwork-rpcs3.sprx";
            const string sprxPath = pluginsFolder + sprxName;

            this.Pipeline.Accessor!.CreateDirectoryIfNotExists(pluginsFolder);

            if (this.Pipeline.Accessor.FileExists(sprxPath))
                this.Pipeline.Accessor.RemoveFile(sprxPath);
            
            this.Progress = 0.5f;
            
            string localSprxName = this.Pipeline.Accessor is EmulatorPatchAccessor
                ? sprxNameEmulator
                : sprxName;

            if (File.Exists(localSprxName))
            {
                State.Logger.LogInfo(Patchwork, "Found custom patchwork.sprx next to exe file, uploading that instead");
                this.Pipeline.Accessor.UploadFile(localSprxName, sprxPath);
            }
            else
            {
                await using Stream writeStream = this.Pipeline.Accessor.OpenWrite(sprxPath);
                await using Stream? readStream = Assembly.GetExecutingAssembly().GetManifestResourceStream(localSprxName);

                if (readStream == null)
                    throw new InvalidOperationException($"The sprx file for {this.Pipeline.Accessor.GetType().Name} is missing from this build!" +
                                                        $"Please tell a developer on Discord/GitHub!");

                await readStream.CopyToAsync(writeStream, cancellationToken);
                await writeStream.FlushAsync(cancellationToken);
            }
        });
    }
}