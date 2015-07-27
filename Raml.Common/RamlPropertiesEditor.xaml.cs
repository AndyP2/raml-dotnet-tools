﻿using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using Raml.Common.Annotations;

namespace Raml.Common
{
    /// <summary>
    /// Interaction logic for RamlPropertiesEditor.xaml
    /// </summary>
    public partial class RamlPropertiesEditor : INotifyPropertyChanged
    {
        private bool isServerUseCase;

        private string ramlPath;
        private string ns;
        private string source;
        private string clientName;
        private bool useAsyncMethods;
        private Visibility serverVisibility;

        public string Namespace
        {
            get { return ns; }
            set
            {
                ns = value;
                OnPropertyChanged();
            }
        }

        public string Source
        {
            get { return source; }
            set
            {
                source = value; 
                OnPropertyChanged();
            }
        }

        public string ClientName
        {
            get { return clientName; }
            set
            {
                clientName = value;
                OnPropertyChanged();
            }
        }

        public bool UseAsyncMethods
        {
            get { return useAsyncMethods; }
            set
            {
                useAsyncMethods = value;
                OnPropertyChanged();
            }
        }

        public Visibility ServerVisibility
        {
            get { return isServerUseCase ? Visibility.Visible : Visibility.Collapsed; }
        }

        public Visibility ClientVisibility
        {
            get { return isServerUseCase ? Visibility.Collapsed : Visibility.Visible; }
        }

        public RamlPropertiesEditor()
        {
            InitializeComponent();
        }

        public void Load(string ramlPath, string serverPath, string clientPath)
        {
            this.ramlPath = ramlPath;
            if (ramlPath.Contains(serverPath) && !ramlPath.Contains(clientPath))
                isServerUseCase = true;

            var ramlProperties = RamlPropertiesManager.Load(ramlPath);
            Namespace = ramlProperties.Namespace;
            Source = ramlProperties.Source;
            if (isServerUseCase)
                UseAsyncMethods = ramlProperties.UseAsyncMethods;
            else
                ClientName = ramlProperties.ClientName;

            OnPropertyChanged("ServerVisibility");
            OnPropertyChanged("ClientVisibility");
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            var ramlProperties = new RamlProperties
            {
                Namespace = Namespace,
                Source = Source,
                ClientName = ClientName,
                UseAsyncMethods = UseAsyncMethods
            };
            
            RamlPropertiesManager.Save(ramlProperties, ramlPath);
            Close();
        }

        private void CancelButton_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            Close();
        }

        public event PropertyChangedEventHandler PropertyChanged;

        [NotifyPropertyChangedInvocator]
        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            var handler = PropertyChanged;
            if (handler != null) handler(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
