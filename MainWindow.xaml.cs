
using Esri.ArcGISRuntime.Geometry;
using Esri.ArcGISRuntime.LocalServices;
using Esri.ArcGISRuntime.Mapping;
using Esri.ArcGISRuntime.Tasks;
using Esri.ArcGISRuntime.Tasks.Geoprocessing;
using Esri.ArcGISRuntime.UI;
using Esri.ArcGISRuntime.UI.Controls;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;


namespace ContourExample
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        string appRootDir;
        string appDataDir;
        string appTempDir;

        // Raster file 
        string testImage;

        // Geoprocessing package
        string gpPackage;

        // Map package
        string mapPackage;

        // Local server URL
        string localServerURL;

        // Geoprocessing service variables
        String localGPserviceUrl;
        GeoprocessingServiceType gpServiceType =
            GeoprocessingServiceType.AsynchronousSubmitWithMapServiceResult;
        //  GeoprocessingServiceType.AsynchronousSubmitWithMapServiceResult;
        GeoprocessingExecutionType gpExecutionType =
            GeoprocessingExecutionType.AsynchronousSubmit;

        private LocalGeoprocessingService _localGPservice;
        private GeoprocessingTask _gpTask;
        private GeoprocessingJob _gpJob;

        // Map service variables
        private LocalMapService _localMapService;
        string localMapServiceURL;

        // Raster workspace variables
        ArcGISMapImageSublayer _rasterSublayer;
        RasterWorkspace rasterWorkspace;

        public MainWindow()
        {
            InitializeComponent();

            // set up the sample
            Initialize();
        }

        private async void Initialize()
        {
            // Set paths that are relative to execution path
            string currentDir = Directory.GetCurrentDirectory();
            int idx = currentDir.IndexOf("bin") - 1;
            appRootDir = currentDir.Substring(0, idx);
            appDataDir = appRootDir + @"\Data";
            appTempDir = appRootDir + @"\temp";

            // Set up files
            testImage = appDataDir + @"\sampleFile.tiff";
            gpPackage = appDataDir + @"\CreateMapTilePackage.gpkx";
            mapPackage = appDataDir + @"\emptyMapPackage.mpkx";

            Debug.WriteLine(">> App Root Directory = " + appRootDir);
            Debug.WriteLine(">> App Data Directory = " + appDataDir);
            Debug.WriteLine(">> App Temp Directory = " + appTempDir);

            ////////////// start Q Basket set up //////////////////
            // Create raster layer from a raster file (Geotiff)
            Debug.WriteLine("Loading raster layer from " + testImage);
            RasterLayer inRasterLayer = new RasterLayer(testImage);

            // Load Raster into Raster Layer
            try
            {
                await inRasterLayer.LoadAsync();
                if (inRasterLayer.LoadStatus != Esri.ArcGISRuntime.LoadStatus.Loaded)
                    Debug.WriteLine("Error - Input Raster Layer not loaded ");
            }
            catch (Exception ex)
            {
                string msg = "Unable to load the raster\n";
                msg += "Raster file = " + testImage;
                msg += "Load status = " + inRasterLayer.LoadStatus.ToString();
                msg += "\n\nMessage: " + ex.Message;
                MessageBox.Show(msg, "inRasterLayer.LoadAsync failed");
            }

            // Create a new EnvelopeBuilder from the full extent of the raster layer.
            // Add a small zoom to make sure entire map is viewable
            EnvelopeBuilder envelopeBuilder = new EnvelopeBuilder(inRasterLayer.FullExtent);
            envelopeBuilder.Expand(0.75);

            // Create a basemap from the raster layer
            Basemap baseMap = new Basemap(inRasterLayer);

            // Create a new map using the new basemap
            Map newMap = new Map(baseMap);

            // Set the viewpoint of the map to the proper extent.
            newMap.InitialViewpoint = new Viewpoint(envelopeBuilder.ToGeometry().Extent);

            // Create a map and add it to the view
            MyMapView.Map = newMap;

            // Load new map to display basemap
            try
            {
                // Add map to the map view.
                MyMapView.Map = newMap;

                // Wait for the map to load.
                await newMap.LoadAsync();
            }
            catch (Exception ex)
            {
                string msg = "Unable to load the Map\n";
                msg += "\n\nMessage: " + ex.Message;
                MessageBox.Show(msg, "newMap.LoadAsync failed");
            }

            // Wait for rendering to finish before taking the screenshot for the thumbnail
            await WaitForRenderCompleteAsync(MyMapView);
            ////////////// end Q Basket set up //////////////////
         
            // Start the Local Server
            try
            {
                // LocalServer must not be running when setting the data path.
                if (LocalServer.Instance.Status == LocalServerStatus.Started)
                {
                    await LocalServer.Instance.StopAsync();
                }

                // Set the local data path - must be done before starting.
                // Avoid Windows path length limitations (260).
                // CreateDirectory won't overwrite if it already exists.
                Directory.CreateDirectory(appTempDir); 
                LocalServer.Instance.AppDataPath = appTempDir;

                // Start the local server instance
                await LocalServer.Instance.StartAsync();
                MessageBox.Show("Local Server started");
                Debug.WriteLine(">> Local Server started");

                // Get the URL for the localServer
                // localhost port is variable 
                localServerURL = LocalServer.Instance.Url.AbsoluteUri;

                Debug.WriteLine("\n>> Local server url - " + localServerURL);
                Debug.WriteLine(">> Local server App Data Path - " +
                                 LocalServer.Instance.AppDataPath);
            }
            catch (Exception ex)
            {
                string msg = "Please ensure the local server is installed \nand configured correctly";
                msg += String.Format("\nMessage: {0}", ex.Message);
                MessageBox.Show(msg, "Local Server failed to start");
                Debug.WriteLine(msg);
                App.Current.Shutdown();
            }

            // LOCAL MAP SERVICE INIT
            // Create and start the local map service
            try
            {
                _localMapService = new LocalMapService(mapPackage);
            }
            catch (Exception ex)
            {
                string msg = "Cannot create the local map service";
                msg += "Map Package = " + mapPackage;
                msg += String.Format("\nMessage: {0}", ex.Message);
                MessageBox.Show(msg, "Local Map Server failed to start");
                Debug.WriteLine(msg);
                App.Current.Shutdown();
            }

            // RASTER WORKSPACE CREATION
            // Create the Raster workspace; this workspace name was chosen arbitrarily
            // Does workspace need to be the same directory as rasters?
            // setting to temp directory
            rasterWorkspace = new RasterWorkspace("raster_wkspc", appTempDir);
            Debug.WriteLine(">> raster workspace folder = " + rasterWorkspace.FolderPath);
            Debug.WriteLine(">> raster workspace id = " + rasterWorkspace.Id);

            // Create the layer source that represents the Raster on disk
            RasterSublayerSource source = new RasterSublayerSource(rasterWorkspace.Id, testImage);

            // Create a sublayer instance from the table source
            _rasterSublayer = new ArcGISMapImageSublayer(0, source);

            // Add the dynamic workspace to the map service
            _localMapService.SetDynamicWorkspaces(new List<DynamicWorkspace>() { rasterWorkspace });
           
            // Register map service status chagne event handle
            _localMapService.StatusChanged += _localMapService_StatusChanged;

            // Start the map service
            try
            {
                await _localMapService.StartAsync();
            }
            catch (Exception ex)
            {
                string msg = "Cannot start the local map service";
                msg += "Map Package = " + mapPackage;
                msg += String.Format("\nMessage: {0}", ex.Message);
                MessageBox.Show(msg, "Local Map Server failed to start");
                Debug.WriteLine(msg);
                App.Current.Shutdown();
            }

            // Get the url to the local map service
            localMapServiceURL = _localMapService.Url.AbsoluteUri;
            MessageBox.Show("Local Map Service URL = " + localMapServiceURL);
            Debug.WriteLine("Local Map Service URL = " + localMapServiceURL);

            // LOCAL GEOPROCESSING SERVICE INIT
            // Create the geoprocessing service
            _localGPservice = new LocalGeoprocessingService(gpPackage, gpServiceType);

            // Ass GP service status chagned event handler
            _localGPservice.StatusChanged += GpServiceOnStatusChanged;

            // Try to start the service
            try
            {
                // Start the service
                await _localGPservice.StartAsync();

                if (_localGPservice.Status == LocalServerStatus.Failed)
                {
                    string msg = ("Geoprocessing service failed to start.\n");
                    MessageBox.Show(msg, "gpService.StartAsync failed");
                    App.Current.Shutdown();
                }
                else if (_localGPservice.Status == LocalServerStatus.Started)
                {
                    localGPserviceUrl = _localGPservice.Url.AbsoluteUri + "/CreateMapTilePackage";

                    string msg = ("Geoprocessing service started.\n");
                    msg += "\n>> GP Service URL: " + localGPserviceUrl;
                    msg += ">> GP Service Max Records: " + _localGPservice.MaxRecords;
                    msg += ">> GP Service Package Path: " + _localGPservice.PackagePath;
                    msg += ">> GP Service Type: " + _localGPservice.ServiceType;
                    MessageBox.Show(msg, "gpService.StartAsync started");
                
                    Debug.WriteLine("\n>> GP Service URL: " + localGPserviceUrl);
                    Debug.WriteLine(">> GP Service Max Records: " + _localGPservice.MaxRecords);
                    Debug.WriteLine(">> GP Service Package Path: " + _localGPservice.PackagePath);
                    Debug.WriteLine(">> GP Service Type: " + _localGPservice.ServiceType);
                }
            }
            catch (Exception ex)
            {
                string msg = ("Geoprocessing service failed to start.\n");
                msg += "\nGeoprocessing package - " + gpPackage + "\n";
                msg += String.Format("\nMessage: {0}", ex.Message);
                MessageBox.Show(msg, "gpService.StartAsync failed");
                return;
            }

            // GEOPROCESSING TASK INIT
            // Create the geoprocessing task from the service
            try
            {
                string url = _localGPservice.Url + "/CreateMapTilePackage";
                _gpTask = await GeoprocessingTask.CreateAsync(new Uri(url));
            }
            catch (Exception ex)
            {
                string msg = ("Geoprocessing task failed to start.\n");
                msg += "\nlocalGPserviceUrl- " + localGPserviceUrl + "\n";
                msg += String.Format("\nMessage: {0}", ex.Message);
                MessageBox.Show(msg, "GeoprocessingTask.CreateAsync failed");
                return;
            }
            MessageBox.Show("GeoprocessingTask.CreateAsync created");

            // GEOPROCESSING JOB 
            // Create the geoprocessing parameters
            GeoprocessingParameters gpParams = new GeoprocessingParameters(gpExecutionType);

            // Add the interval parameter to the geoprocessing parameters
            //GeoprocessingString Input_Map = new GeoprocessingString("MyMapView.Map");
            GeoprocessingString Input_Map = new GeoprocessingString("localMapServiceURL");
            GeoprocessingDouble Max_LOD = new GeoprocessingDouble(10);
            GeoprocessingString  Output_Package = new GeoprocessingString("C://Karen/Data/TilePackages/test.tpkx");
            
            gpParams.Inputs.Add("Input_Map", Input_Map);
            gpParams.Inputs.Add("Max_LOD", Max_LOD);
            gpParams.Inputs.Add("Output_Package", Output_Package);
           
            // Create the job
            try
            {
                _gpJob = _gpTask.CreateJob(gpParams);
            }
            catch (Exception ex)
            {
                string msg = ("Geoprocessing job cannot be created.\n");
                msg += String.Format("\nMessage: {0}", ex.Message);
                MessageBox.Show(msg, "_gpTask.CreateJob failed");
                return;
            }
            MessageBox.Show("GeoprocessingTask.CreateJob created");
            MyLoadingIndicator.Visibility = Visibility.Visible;

            // Update the UI when job progress changes
            _gpJob.ProgressChanged += (sender, args) =>
            {
                Dispatcher.Invoke(() => { MyLoadingIndicator.Value = _gpJob.Progress; });
            };

            // Be notified when the task completes (or other change happens)
            _gpJob.JobChanged += GpJobOnJobChanged;

            // Start the job
            try
            {
                _gpJob.Start();
            }
            catch (Exception ex)
            {
                string msg = ("Geoprocessing start job failed to start.\n");
                msg += String.Format("\nMessage: {0}", ex.Message);
                MessageBox.Show(msg, "_gpjob.Start failed");
                return;
            }
            MessageBox.Show("GeoprocessingTask job started");
        }


        private void GpServiceOnStatusChanged(object sender, StatusChangedEventArgs statusChangedEventArgs)
        {
            // Return if the server hasn't started
            if (statusChangedEventArgs.Status != LocalServerStatus.Started) 
                return;
        }


        private void GpJobOnJobChanged(object o, EventArgs eventArgs)
        {
            // Show message if job failed
            if (_gpJob.Status == JobStatus.Failed)
            {
                string msg = "Geoprocessing job Failed\n";
                msg += _gpJob.Error;
                MessageBox.Show(msg);
                return;
            }

            // Return if not succeeded
            if (_gpJob.Status != JobStatus.Succeeded) 
            {
                string msg = "Geoprocessing job did not succeed\n";
                msg += "\n\nJob status = " + _gpJob.Status.ToString();
                MessageBox.Show(msg);
                return;
            }

            // OUTPUT THE RESULTS
            // Get the URL to the map service
            // string gpServiceResultUrl = _localGPservice.Url.ToString();
            
            // Get the URL segment for the specific job results
            // string jobSegment = "MapServer/jobs/" + _gpJob.ServerJobId;

            // Update the URL to point to the specific job from the service
            // gpServiceResultUrl = gpServiceResultUrl.Replace("GPServer", jobSegment);
            // MessageBox.Show("Result url = " + gpServiceResultUrl);

            // Create a map image layer to show the results
            // ArcGISMapImageLayer myMapImageLayer = new ArcGISMapImageLayer(new Uri(gpServiceResultUrl));

            // Load the layer
            // await myMapImageLayer.LoadAsync();

            // This is needed because the event comes from outside of the UI thread
            Dispatcher.Invoke(() =>
            {
                // Add the layer to the map
                // MyMapView.Map.OperationalLayers.Add(myMapImageLayer);

                // Hide the progress bar
                MyLoadingIndicator.Visibility = Visibility.Collapsed;

            });
        }

        private async void _localMapService_StatusChanged(object sender, StatusChangedEventArgs e)
        {
            // Return if the service hasn't started yet
            if (e.Status != LocalServerStatus.Started) return;

            // Create the imagery layer
            ArcGISMapImageLayer imageryLayer = new ArcGISMapImageLayer(_localMapService.Url);

            // Subscribe to image layer load status change events
            // Only set up the sublayer renderer for the Raster after the parent layer has finished loading
            imageryLayer.LoadStatusChanged += (q, ex) =>
            {
                // Add the layer to the map once loaded
                if (ex.Status == Esri.ArcGISRuntime.LoadStatus.Loaded)
                {
                    // Add the Raster sublayer to the imagery layer
                    imageryLayer.Sublayers.Add(_rasterSublayer);
                }
            };

            // Load the layer
            await imageryLayer.LoadAsync();

            // Clear any existing layers
            MyMapView.Map.OperationalLayers.Clear();

            // Add the image layer to the map
            MyMapView.Map.OperationalLayers.Add(imageryLayer);
        }


        private static Task WaitForRenderCompleteAsync(MapView mapview)
        {
            // The task completion source manages the task, including 
            // marking it as finished.
            TaskCompletionSource<object> tcs = new TaskCompletionSource<object>();

            // If the map is currently finished drawing, set the result immediately.
            if (mapview.DrawStatus == DrawStatus.Completed)
            {
                tcs.SetResult(null);
            }
            // Otherwise, configure a callback and a timeout to either set the result when
            // the map is finished drawing or set the result after 2000 ms.
            else
            {
                // Define a cancellation token source for 2000 ms.
                const int timeoutMs = 2000;
                var ct = new CancellationTokenSource(timeoutMs);

                // Register the callback that sets the task result after 2000 ms.
                ct.Token.Register(() =>
                    tcs.TrySetResult(null), false);

                // Define a local function that will set the task result and 
                // unregister itself when the map finishes drawing.
                void DrawCompleteHandler(object s, DrawStatusChangedEventArgs e)
                {
                    if (e.Status == DrawStatus.Completed)
                    {
                        mapview.DrawStatusChanged -= DrawCompleteHandler;
                        tcs.TrySetResult(null);
                    }
                }

                // Register the draw complete event handler.
                mapview.DrawStatusChanged += DrawCompleteHandler;
            }

            // Return the task.
            return tcs.Task;
        }   // end WaitForRenderCompleteAsync
    }   // end class Mainwindow
}   // end namespace
