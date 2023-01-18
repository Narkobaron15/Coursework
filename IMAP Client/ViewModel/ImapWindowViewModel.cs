﻿using CommunityToolkit.Mvvm.Collections;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HelperLibrary.DAL;
using IMAP_Client.View;
using MailKit;
using MailKit.Net.Imap;
using MailKit.Search;
using MimeKit;
using Org.BouncyCastle.Asn1.X509;
using SMTP_Client.View;
using SMTP_Client.ViewModel;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace IMAP_Client.ViewModel
{
    public partial class ImapWindowViewModel : ObservableObject
    {
        private static IDictionary<string, SearchQuery> m_Queries { get; }
        public static IEnumerable<string> StrQueries => m_Queries.Keys;
        public ObservableCollection<MimeMessage> Messages { get; }
        private ObservableCollection<IMailFolder> MailFolders { get; }
        public ObservableCollection<string> Folders { get; }

        public IRelayCommand SearchCmd { get; }
        public IRelayCommand WriteCmd { get; }
        public IRelayCommand OpenLetterCmd { get; }

        private IImapClient Client { get; }
        private string? pwd;
        public MimeMessage? SelectedMessage { get; set; }

        [ObservableProperty, NotifyCanExecuteChangedFor(nameof(SearchCmd))]
        private string m_SearchKey;
        private UserCredentials? creds;

        private string? m_SelectedQuery;
        public string SelectedQuery
        {
            get => m_SelectedQuery ?? string.Empty;
            set
            {
                Cancel?.Cancel();
                SetProperty(ref m_SelectedQuery, value, nameof(SelectedQuery));
                SearchCmd.NotifyCanExecuteChanged();
                Messages.Clear();

                Task.Run(async () =>
                {
                    if (value != null)
                    {
                        await Task.Delay(1000);
                        ExecuteSearch(m_Queries[value]);
                    }
                });
            }
        }

        private string? m_SelectedFolder;
        public string SelectedStrFolder
        {
            get => m_SelectedFolder!;
            set
            {
                Cancel?.Cancel();
                Messages.Clear();

                Task.Run(async () =>
                {
                    await Task.Delay(1000);
                    SelectedFolder?.Close();
                    m_SelectedFolder = value;
                    SelectedFolder = MailFolders.Where(x => x.Name == value).FirstOrDefault();
                    SelectedFolder?.Open(FolderAccess.ReadWrite);
                    ExecuteSearch(SearchQuery.All);
                });
            }
        }

        private IMailFolder? SelectedFolder;

        private CancellationTokenSource? _cancelloadingsource;
        private CancellationTokenSource? Cancel
        {
            get => _cancelloadingsource;
            set
            {
                _cancelloadingsource?.Dispose();
                _cancelloadingsource = value;
            }
        }
        private CancellationToken CancelToken => Cancel?.Token ?? default;

        private Dispatcher CurrentDispatcher { get; }

        static ImapWindowViewModel()
        {
            m_Queries = new Dictionary<string, SearchQuery>()
            {
                { "All", SearchQuery.All },
                { "Flagged", SearchQuery.Flagged}, 
                { "Not flagged", SearchQuery.NotFlagged },
                { "Deleted", SearchQuery.Deleted }, 
                { "Answered", SearchQuery.Answered },
                { "Seen", SearchQuery.Seen },
                { "Not seen", SearchQuery.NotSeen },
                { "Old", SearchQuery.Old },
                { "Recent", SearchQuery.Recent }
            };
        }
        public ImapWindowViewModel()
        {
            Messages = new();
            MailFolders = new();
            Folders = new();
            Client = new ImapClient();

            SearchCmd = new RelayCommand(ExecuteSearchCmd, CanExecuteSearchCmd);
            WriteCmd = new AsyncRelayCommand(ExecuteWriteCmd);
            OpenLetterCmd = new RelayCommand<MimeMessage>(ExecuteOpenLetterCmd);

            m_SearchKey = string.Empty;
            SelectedQuery = "All";

            CurrentDispatcher = Dispatcher.CurrentDispatcher;

            GC.Collect(GC.MaxGeneration);
        }

        private void ExecuteOpenLetterCmd(MimeMessage? obj)
        {
            if (obj == null) return;

            ViewEmailWindow wnd = new();
            var VM = (ViewEmailWindowViewModel)wnd.DataContext;
            VM.Load(obj);
            wnd.ShowDialog();

            SelectedMessage = null;
        }

        private async Task ExecuteWriteCmd()
        {
            SmtpWindow wnd = new();
            var VM = (SmtpWindowViewModel)wnd.DataContext;
            await VM.Auth(creds!, pwd!);
            wnd.ShowDialog();
        }

        private async void ExecuteSearchCmd()
        {
            Cancel?.Cancel();
            await Task.Delay(1000);
            await CurrentDispatcher.InvokeAsync(Messages.Clear);
            ExecuteSearch(SearchKey);
        }
        private bool CanExecuteSearchCmd()
            => !string.IsNullOrWhiteSpace(SearchKey);

        public async Task Auth(UserCredentials credentials, string pwd)
        {
            creds = credentials;
            ServerData? ImapData = credentials.Servers.ImapServer;

            if (ImapData is null) throw new ArgumentException("Servers.ImapServer property may not be null to auth", nameof(credentials));

            await Client.ConnectAsync(ImapData.Host, ImapData.Port, ImapData.SecurityOptions);
            await Client.AuthenticateAsync(new NetworkCredential(credentials.UserName, pwd));

            foreach (var ns in Client.PersonalNamespaces)
            {
                var folders = Client.GetFolders(ns)
                                    .Where(x => (x.Attributes & FolderAttributes.NonExistent) != FolderAttributes.NonExistent);
                foreach (var folder in folders)
                {
                    Folders.Add(folder.Name);
                    MailFolders.Add(folder);
                }
            }

            SelectedStrFolder = Folders.First();
            this.pwd = pwd;
        }

        private static SearchQuery AggregateQueriesByTerm(string term)
        {
            term = term.Trim();

            SearchQuery[] queries = new[]
            {
                SearchQuery.BccContains(term),
                SearchQuery.CcContains(term),
                SearchQuery.ToContains(term),
                SearchQuery.FromContains(term),
                SearchQuery.BodyContains(term),
                SearchQuery.SubjectContains(term),
                SearchQuery.MessageContains(term),
            };

            SearchQuery result = queries[0];
            foreach (SearchQuery query in queries)
                result = result.Or(query);

            return result;
        }

        private void ExecuteSearch(string query)
        {
            query = query.Trim();
            ExecuteSearch(
                AggregateQueriesByTerm(query)
                .Or(AggregateQueriesByTerm(query.ToLower()))
                );
        }
        private void ExecuteSearch(SearchQuery query)
        {
            if (SelectedFolder == null) return;

            lock (Client.SyncRoot)
            {
                Cancel = new();

                var idlist = SelectedFolder.Search(query).Reverse().ToArray();

                if (idlist.Length == 0)
                    MessageBox.Show("No items found >.<", "Information", MessageBoxButton.OK, MessageBoxImage.Information);

                MimeMessage msg;
                foreach (var id in idlist)
                {
                    if (CancelToken.IsCancellationRequested)
                        return;

                    msg = SelectedFolder.GetMessage(id);
                    CurrentDispatcher.InvokeAsync(() => Messages.Add(msg), DispatcherPriority.Normal, CancelToken);
                }
            }

            if (CancelToken.IsCancellationRequested)
                Cancel = new();
        }

        ~ImapWindowViewModel()
        {
            Client.Disconnect(true);
        }
    }
}
