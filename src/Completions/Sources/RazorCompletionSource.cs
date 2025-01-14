﻿using Microsoft.VisualStudio.Imaging;
using Microsoft.VisualStudio.Imaging.Interop;
using Microsoft.VisualStudio.Language.Intellisense;
using Microsoft.VisualStudio.Language.Intellisense.AsyncCompletion;
using Microsoft.VisualStudio.Language.Intellisense.AsyncCompletion.Data;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TailwindCSSIntellisense.Settings;

namespace TailwindCSSIntellisense.Completions.Sources
{
    /// <summary>
    /// Completion provider for all HTML content files to provide Intellisense support for TailwindCSS classes
    /// </summary>
    internal class RazorCompletionSource : ICompletionSource
    {
        private readonly CompletionUtilities _completionUtils;
        private readonly SettingsProvider _settingsProvider;
        private readonly IAsyncCompletionBroker _asyncCompletionBroker;
        private readonly ICompletionBroker _completionBroker;
        private readonly ITextBuffer _textBuffer;

        private bool? _showAutocomplete;
        private bool _initializeSuccess = true;

        public RazorCompletionSource(CompletionUtilities completionUtils, SettingsProvider settingsProvider, IAsyncCompletionBroker asyncCompletionBroker, ICompletionBroker completionBroker, ITextBuffer textBuffer)
        {
            _completionUtils = completionUtils;
            _settingsProvider = settingsProvider;
            _asyncCompletionBroker = asyncCompletionBroker;
            _completionBroker = completionBroker;
            _textBuffer = textBuffer;

            _asyncCompletionBroker.CompletionTriggered += OnAsyncCompletionSessionStarted;
            _settingsProvider.OnSettingsChanged += SettingsChangedAsync;
        }

        /// <summary>
        /// Overrides the original completion set to include TailwindCSS classes
        /// </summary>
        /// <param name="session">Provided by Visual Studio</param>
        /// <param name="completionSets">Provided by Visual Studio</param>

        void ICompletionSource.AugmentCompletionSession(ICompletionSession session, IList<CompletionSet> completionSets)
        {
            if (_showAutocomplete == null)
            {
                _showAutocomplete = ThreadHelper.JoinableTaskFactory.Run(_settingsProvider.GetSettingsAsync).EnableTailwindCss;
            }

            if (_showAutocomplete == false || _completionUtils.Scanner.HasConfigurationFile == false)
            {
                return;
            }

            if (!_completionUtils.Initialized || _initializeSuccess == false)
            {
                _initializeSuccess = ThreadHelper.JoinableTaskFactory.Run(() => _completionUtils.InitializeAsync());

                if (_initializeSuccess == false)
                {
                    return;
                }
            }

            if (IsInClassScope(session, out string classAttributeValueUpToPosition) == false)
            {
                return;
            }

            var position = session.TextView.Caret.Position.BufferPosition.Position;
            ITextSnapshot snapshot = _textBuffer.CurrentSnapshot;
            var triggerPoint = (SnapshotPoint)session.GetTriggerPoint(snapshot);

            if (triggerPoint == null)
                return;

            var line = triggerPoint.GetContainingLine();
            SnapshotPoint start = triggerPoint;

            var currentClassTotal = classAttributeValueUpToPosition.Split(' ').Last();

            var completions = ClassCompletionGeneratorHelper.GetCompletions(currentClassTotal, _completionUtils);

            var applicableTo = GetApplicableTo(triggerPoint, snapshot);

            if (completionSets.Count == 1)
            {
                var defaultCompletionSet = completionSets[0];

                // Must convert defaultCompletionSet.Completions to a list because original list is read only
                var newCompletionList = defaultCompletionSet.Completions.ToList().Concat(completions);

                var overridenCompletionSet = new TailwindCssCompletionSet(
                    defaultCompletionSet.Moniker,
                    defaultCompletionSet.DisplayName,
                    applicableTo,
                    newCompletionList,
                    defaultCompletionSet.CompletionBuilders);

                // Overrides the original completion set so there aren't two different completion tabs
                completionSets.Clear();
                completionSets.Add(overridenCompletionSet);
            }
            else
            {
                completionSets.Add(new TailwindCssCompletionSet(
                    "All",
                    "All",
                    applicableTo,
                    completions,
                    new List<Completion>()));
            }
        }

        private void OnAsyncCompletionSessionStarted(object sender, CompletionTriggeredEventArgs e)
        {
            var sessions = _completionBroker.GetSessions(e.TextView);

            var tailwindSession = sessions.FirstOrDefault(s => s.SelectedCompletionSet is TailwindCssCompletionSet);

            if (tailwindSession != null && IsInClassScope(tailwindSession, out string classText) && tailwindSession.SelectedCompletionSet is TailwindCssCompletionSet tailwindCompletionSet)
            {
                var newCompletions = e.CompletionSession.GetComputedItems(default)
                    .Items
                    .Where(c => c.DisplayText.StartsWith(classText.Split(' ').Last(), StringComparison.InvariantCultureIgnoreCase))
                    .Select(c => new Completion3(c.DisplayText, c.InsertText, null, c.Icon == null ? KnownMonikers.LocalVariable : new ImageMoniker() { Guid = c.Icon.ImageId.Guid, Id = c.Icon.ImageId.Id }, null));

                tailwindCompletionSet.AddCompletions(newCompletions);
                e.CompletionSession?.Dismiss();
                // Prevent word cutoff by re-rendering completion GUI
                tailwindSession.Filter();
            }
        }

        private bool IsInClassScope(ICompletionSession session, out string classText)
        {
            var startPos = new SnapshotPoint(session.TextView.TextSnapshot, 0);
            var caretPos = session.TextView.Caret.Position.BufferPosition;

            var searchSnapshot = new SnapshotSpan(startPos, caretPos);
            var text = searchSnapshot.GetText();

            var indexOfCurrentClassAttribute = text.LastIndexOf("class=\"", StringComparison.InvariantCultureIgnoreCase);
            if (indexOfCurrentClassAttribute == -1)
            {
                classText = null;
                return false;
            }
            var quotationMarkAfterLastClassAttribute = text.IndexOf('\"', indexOfCurrentClassAttribute);
            var lastQuotationMark = text.LastIndexOf('\"');

            if (lastQuotationMark == quotationMarkAfterLastClassAttribute)
            {
                classText = text.Substring(lastQuotationMark + 1);
                return true;
            }
            else
            {
                classText = null;
                return false;
            }
        }

        private ITrackingSpan GetApplicableTo(SnapshotPoint triggerPoint, ITextSnapshot snapshot)
        {
            SnapshotPoint end = triggerPoint;
            SnapshotPoint start = triggerPoint - 1;

            while (start.GetChar() != '"' && start.GetChar() != ' ')
            {
                start -= 1;
            }

            start += 1;

            return snapshot.CreateTrackingSpan(new SnapshotSpan(start, end), SpanTrackingMode.EdgeInclusive);
        }

        public void Dispose()
        {
            _asyncCompletionBroker.CompletionTriggered -= OnAsyncCompletionSessionStarted;
            _settingsProvider.OnSettingsChanged -= SettingsChangedAsync;
        }

        private Task SettingsChangedAsync(TailwindSettings settings)
        {
            _showAutocomplete = settings.EnableTailwindCss;
            return Task.CompletedTask;
        }
    }
}
