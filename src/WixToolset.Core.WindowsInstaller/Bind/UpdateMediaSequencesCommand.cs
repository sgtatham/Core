// Copyright (c) .NET Foundation and contributors. All rights reserved. Licensed under the Microsoft Reciprocal License. See LICENSE.TXT file in the project root for full license information.

namespace WixToolset.Core.WindowsInstaller.Bind
{
    using System.Collections.Generic;
    using System.Linq;
    using WixToolset.Core.Bind;
    using WixToolset.Data;
    using WixToolset.Data.Symbols;

    internal class UpdateMediaSequencesCommand
    {
        public UpdateMediaSequencesCommand(IntermediateSection section, List<FileFacade> fileFacades)
        {
            this.Section = section;
            this.FileFacades = fileFacades;
        }

        private IntermediateSection Section { get; }

        private List<FileFacade> FileFacades { get; }

        public void Execute()
        {
            var mediaRows = this.Section.Symbols.OfType<MediaSymbol>().ToDictionary(t => t.DiskId);

            // Calculate sequence numbers and media disk id layout for all file media information objects.
            if (SectionType.Module == this.Section.Type)
            {
                var lastSequence = 0;

                foreach (var facade in this.FileFacades)
                {
                    facade.Sequence = ++lastSequence;
                }
            }
            else
            {
                var lastSequence = 0;
                MediaSymbol mediaSymbol = null;
                var patchGroups = new Dictionary<int, List<FileFacade>>();

                // sequence the non-patch-added files
                foreach (var facade in this.FileFacades)
                {
                    if (null == mediaSymbol)
                    {
                        mediaSymbol = mediaRows[facade.DiskId];
                        if (SectionType.Patch == this.Section.Type)
                        {
                            // patch Media cannot start at zero
                            lastSequence = mediaSymbol.LastSequence ?? 1;
                        }
                    }
                    else if (mediaSymbol.DiskId != facade.DiskId)
                    {
                        mediaSymbol.LastSequence = lastSequence;
                        mediaSymbol = mediaRows[facade.DiskId];
                    }

                    if (facade.PatchGroup.HasValue)
                    {
                        if (patchGroups.TryGetValue(facade.PatchGroup.Value, out var patchGroup))
                        {
                            patchGroup = new List<FileFacade>();
                            patchGroups.Add(facade.PatchGroup.Value, patchGroup);
                        }

                        patchGroup.Add(facade);
                    }
                    else if (!facade.FromModule)
                    {
                        facade.Sequence = ++lastSequence;
                    }
                }

                if (null != mediaSymbol)
                {
                    mediaSymbol.LastSequence = lastSequence;
                    mediaSymbol = null;
                }

                // sequence the patch-added files
                foreach (var patchGroup in patchGroups.Values)
                {
                    foreach (var facade in patchGroup)
                    {
                        if (null == mediaSymbol)
                        {
                            mediaSymbol = mediaRows[facade.DiskId];
                        }
                        else if (mediaSymbol.DiskId != facade.DiskId)
                        {
                            mediaSymbol.LastSequence = lastSequence;
                            mediaSymbol = mediaRows[facade.DiskId];
                        }

                        facade.Sequence = ++lastSequence;
                    }
                }

                if (null != mediaSymbol)
                {
                    mediaSymbol.LastSequence = lastSequence;
                }
            }
        }
    }
}
