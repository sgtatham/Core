// Copyright (c) .NET Foundation and contributors. All rights reserved. Licensed under the Microsoft Reciprocal License. See LICENSE.TXT file in the project root for full license information.

namespace WixToolset.Core
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using WixToolset.Core.Bind;
    using WixToolset.Data;
    using WixToolset.Data.Symbols;
    using WixToolset.Extensibility;
    using WixToolset.Extensibility.Data;
    using WixToolset.Extensibility.Services;

    /// <summary>
    /// Resolver for the WiX toolset.
    /// </summary>
    internal class Resolver : IResolver
    {
        internal Resolver(IWixToolsetServiceProvider serviceProvider)
        {
            this.ServiceProvider = serviceProvider;

            this.Messaging = serviceProvider.GetService<IMessaging>();

            this.VariableResolver = serviceProvider.GetService<IVariableResolver>();
        }

        private IWixToolsetServiceProvider ServiceProvider { get; }

        private IMessaging Messaging { get; }

        private IVariableResolver VariableResolver { get; set; }

        public IEnumerable<IBindPath> BindPaths { get; set; }

        public string IntermediateFolder { get; set; }

        public Intermediate IntermediateRepresentation { get; set; }

        public IEnumerable<Localization> Localizations { get; set; }

        public IEnumerable<string> FilterCultures { get; set; }

        public IResolveResult Resolve(IResolveContext context)
        {
            foreach (var extension in context.Extensions)
            {
                extension.PreResolve(context);
            }

            ResolveResult resolveResult = null;
            try
            {
                var codepage = this.PopulateVariableResolver(context);

                this.LocalizeUI(context);

                resolveResult = this.DoResolve(context, codepage);
            }
            finally
            {
                foreach (var extension in context.Extensions)
                {
                    extension.PostResolve(resolveResult);
                }
            }

            return resolveResult;
        }

        private ResolveResult DoResolve(IResolveContext context, int? codepage)
        {
            var buildingPatch = context.IntermediateRepresentation.Sections.Any(s => s.Type == SectionType.Patch);

            var filesWithEmbeddedFiles = new ExtractEmbeddedFiles();

            IEnumerable<DelayedField> delayedFields;
            {
                var command = new ResolveFieldsCommand();
                command.Messaging = this.Messaging;
                command.BuildingPatch = buildingPatch;
                command.VariableResolver = this.VariableResolver;
                command.BindPaths = context.BindPaths;
                command.Extensions = context.Extensions;
                command.FilesWithEmbeddedFiles = filesWithEmbeddedFiles;
                command.IntermediateFolder = context.IntermediateFolder;
                command.Intermediate = context.IntermediateRepresentation;
                command.SupportDelayedResolution = true;
                command.AllowUnresolvedVariables = context.AllowUnresolvedVariables;
                command.Execute();

                delayedFields = command.DelayedFields;
            }

#if TODO_PATCHING
            if (context.IntermediateRepresentation.SubStorages != null)
            {
                foreach (SubStorage transform in context.IntermediateRepresentation.SubStorages)
                {
                    var command = new ResolveFieldsCommand();
                    command.BuildingPatch = buildingPatch;
                    command.BindVariableResolver = context.WixVariableResolver;
                    command.BindPaths = context.BindPaths;
                    command.Extensions = context.Extensions;
                    command.FilesWithEmbeddedFiles = filesWithEmbeddedFiles;
                    command.IntermediateFolder = context.IntermediateFolder;
                    command.Intermediate = context.IntermediateRepresentation;
                    command.SupportDelayedResolution = false;
                    command.Execute();
                }
            }
#endif

            var expectedEmbeddedFiles = filesWithEmbeddedFiles.GetExpectedEmbeddedFiles();

            context.IntermediateRepresentation.UpdateLevel(IntermediateLevels.Resolved);

            return new ResolveResult
            {
                Codepage = codepage.HasValue ? codepage.Value : -1,
                ExpectedEmbeddedFiles = expectedEmbeddedFiles,
                DelayedFields = delayedFields,
                IntermediateRepresentation = context.IntermediateRepresentation
            };
        }

        /// <summary>
        /// Localize dialogs and controls.
        /// </summary>
        private void LocalizeUI(IResolveContext context)
        {
            foreach (var section in context.IntermediateRepresentation.Sections)
            {
                foreach (var symbol in section.Symbols.OfType<DialogSymbol>())
                {
                    if (this.VariableResolver.TryGetLocalizedControl(symbol.Id.Id, null, out var localizedControl))
                    {
                        if (CompilerConstants.IntegerNotSet != localizedControl.X)
                        {
                            symbol.HCentering = localizedControl.X;
                        }

                        if (CompilerConstants.IntegerNotSet != localizedControl.Y)
                        {
                            symbol.VCentering = localizedControl.Y;
                        }

                        if (CompilerConstants.IntegerNotSet != localizedControl.Width)
                        {
                            symbol.Width = localizedControl.Width;
                        }

                        if (CompilerConstants.IntegerNotSet != localizedControl.Height)
                        {
                            symbol.Height = localizedControl.Height;
                        }

                        symbol.RightAligned |= localizedControl.RightAligned;
                        symbol.RightToLeft |= localizedControl.RightToLeft;
                        symbol.LeftScroll |= localizedControl.LeftScroll;

                        if (!String.IsNullOrEmpty(localizedControl.Text))
                        {
                            symbol.Title = localizedControl.Text;
                        }
                    }
                }

                foreach (var symbol in section.Symbols.OfType<ControlSymbol>())
                {
                    if (this.VariableResolver.TryGetLocalizedControl(symbol.DialogRef, symbol.Control, out var localizedControl))
                    {
                        if (CompilerConstants.IntegerNotSet != localizedControl.X)
                        {
                            symbol.X = localizedControl.X;
                        }

                        if (CompilerConstants.IntegerNotSet != localizedControl.Y)
                        {
                            symbol.Y = localizedControl.Y;
                        }

                        if (CompilerConstants.IntegerNotSet != localizedControl.Width)
                        {
                            symbol.Width = localizedControl.Width;
                        }

                        if (CompilerConstants.IntegerNotSet != localizedControl.Height)
                        {
                            symbol.Height = localizedControl.Height;
                        }

                        symbol.RightAligned |= localizedControl.RightAligned;
                        symbol.RightToLeft |= localizedControl.RightToLeft;
                        symbol.LeftScroll |= localizedControl.LeftScroll;

                        if (!String.IsNullOrEmpty(localizedControl.Text))
                        {
                            symbol.Text = localizedControl.Text;
                        }
                    }
                }
            }
        }

        private int? PopulateVariableResolver(IResolveContext context)
        {
            var localizations = FilterLocalizations(context);
            var codepage = localizations.FirstOrDefault()?.Codepage;

            foreach (var localization in localizations)
            {
                this.VariableResolver.AddLocalization(localization);
            }

            // Gather all the wix variables.
            var wixVariableSymbols = context.IntermediateRepresentation.Sections.SelectMany(s => s.Symbols).OfType<WixVariableSymbol>();
            foreach (var symbol in wixVariableSymbols)
            {
                this.VariableResolver.AddVariable(symbol.SourceLineNumbers, symbol.Id.Id, symbol.Value, symbol.Overridable);
            }

            return codepage;
        }

        private static IEnumerable<Localization> FilterLocalizations(IResolveContext context)
        {
            var result = new List<Localization>();
            var filter = CalculateCultureFilter(context);

            var localizations = context.Localizations.Concat(context.IntermediateRepresentation.Localizations).ToList();

            AddFilteredLocalizations(result, filter, localizations);

            // Filter localizations provided by extensions with data.
            var creator = context.ServiceProvider.GetService<ISymbolDefinitionCreator>();

            foreach (var data in context.ExtensionData)
            {
                var library = data.GetLibrary(creator);

                if (library?.Localizations != null && library.Localizations.Any())
                {
                    var extensionFilter = (!filter.Any() && data.DefaultCulture != null) ? new[] { data.DefaultCulture } : filter;

                    AddFilteredLocalizations(result, extensionFilter, library.Localizations);
                }
            }

            return result;
        }

        private static IEnumerable<string> CalculateCultureFilter(IResolveContext context)
        {
            var filter = context.FilterCultures ?? Array.Empty<string>();

            // If no filter was specified, look for a language neutral localization file specified
            // from the command-line (not embedded in the intermediate). If found, filter on language
            // neutral.
            if (!filter.Any() && context.Localizations.Any(l => String.IsNullOrEmpty(l.Culture)))
            {
                filter = new[] { String.Empty };
            }

            return filter;
        }

        private static void AddFilteredLocalizations(List<Localization> result, IEnumerable<string> filter, IEnumerable<Localization> localizations)
        {
            // If there is no filter, return all localizations.
            if (!filter.Any())
            {
                result.AddRange(localizations);
            }
            else // filter localizations in order specified by the filter
            {
                foreach (var culture in filter)
                {
                    result.AddRange(localizations.Where(l => culture.Equals(l.Culture, StringComparison.OrdinalIgnoreCase) || String.IsNullOrEmpty(l.Culture)));
                }
            }
        }
    }
}
