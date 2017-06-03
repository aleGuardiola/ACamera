//Class to make easier the camera2 api and to be more compatible
//Alejandro Guardiola 2017
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Android.Content;
using Android.Graphics;
using Android.Hardware.Camera2;
using Android.Hardware.Camera2.Params;
using Android.Media;
using Android.OS;
using Android.Runtime;
using Android.Util;
using Android.Views;
using Java.Lang;

namespace Guardiola.Android
{
	public class ACamera : Java.Lang.Object, TextureView.ISurfaceTextureListener, ImageReader.IOnImageAvailableListener
	{
		//The Android Context
		//private Context mContext;
		//The Camera Manager
		private CameraManager mCameraManager;
		//The handler to use
		private Handler mHandler;
		//The camera device
        private CameraDevice mDevice;
		//the id of the camera
		private string _id;
		//The lens facing of the camera
		private LensFacing _lensFacing;
		//The texture View for preview(optional)
		private TextureView _TextureView;
		//The best size for the preview
		private Size mPreviewSize;
		private int mPreviewWidth;
		private int mPreviewHeight;
		//The capture request using
		private CaptureRequest.Builder mCaptureBuilder;
		//The flash mode
		//private FlashMode _flash;
		//if the camera need to be rotate in order to prevent image distorcion
		private bool mRotate;
		//The zoom used
		private float mZoom = 0;
		//file to save the image
		private string mMediaFile;
		//media recorder for videos
		private MediaRecorder mMediaRecorder;

		//the callbacks
		private CameraStateCallBack mCameraStateCallBack;
		//The repeat capture session state callback
		private CaptureSessionStateCallBack mCaptureSessionStateCallBack;
		//The single capture callBack
		private CaptureSessionStateCallBack mSingleCaptureSessionStateCallBack;

		//accessors-----------------------------
        public LensFacing LensFacing
		{
			get
			{
				return _lensFacing;
			}
			set
			{
				_lensFacing = value;
				openCamera();
			}
		}

		public string Id
		{
			get
			{
				return _id;
			}
		}

		public TextureView TextureView
		{ 
		    get
			{
				return _TextureView;
			}
			set
			{
				if (value == null)
				{
					_TextureView.SurfaceTextureListener = null;
					_TextureView = null;
					return;
				}

				_TextureView = value;
				_TextureView.SurfaceTextureListener = this;
				if (_TextureView.IsAvailable && mDevice != null )
				{
					mPreviewSize = getBestSize(getSizesSupported(typeof(SurfaceTexture)), _TextureView.Width, _TextureView.Height);

					adjustAspectRatio(mPreviewSize.Width, mPreviewSize.Height);

					mPreviewWidth = mPreviewSize.Width;
					mPreviewHeight = mPreviewSize.Height;

					startPreview();
				}
			}
		}

		public float Zoom
		{ 
		   get
			{
				return mZoom;
			}
			set
			{
				mZoom = value > 90 ? 90 : value;

				if (mCaptureBuilder != null)
				{
					try
					{
						Log.Debug("CameraActivity", "Zoom: {0}", mZoom);
						mCaptureBuilder.Set(CaptureRequest.ScalerCropRegion, getRectZoom(mCameraManager, mZoom, _id));
					}
					catch (System.Exception e)
					{
						Log.Debug("ACamera", "ZoomError: {0}", e.Message);
					}
				}
			}
		}

		//Called when error
		public event EventHandler<CameraErrorEventArgs> CameraError;

		//Called when Camera Disconected
		public event EventHandler CameraDisconnected;

		//Called when the image is captured
		public event EventHandler<ImageCapturedEventArgs> ImageCaptured;

		//Called when error capturing image
		public event EventHandler<ImageCaptureErrorEventArgs> ImageCaptureError;

		//Called when video is captured
		public event EventHandler<ImageCapturedEventArgs> VideoCaptured;
		//---------------------------------------

		//Call when activity pause
		public void OnPause()
		{
			closeCamera();
		}

		//Call when activity pause
		public void OnResume()
		{
			openCamera();
		}

		//start recording using the preview proportions
		public void StartRecording(string file, FlashMode flashMode)
		{ 
            int width, height;

			if (mRotate)
			{
				width = _TextureView.Height;
				height = _TextureView.Width;
			}
			else
			{
				width = _TextureView.Width;
				height = _TextureView.Height;
			}

			var sizes = getSizesSupported(typeof(MediaRecorder));
			var size = getBestSize(sizes, width, height);

			var c = mCameraManager.GetCameraCharacteristics(_id);
			int rotation = (int)c.Get(CameraCharacteristics.SensorOrientation);

			//if (_lensFacing == LensFacing.Front)
			//	rotation *= -1;

			rotation = getJpegOrientation(rotation, 0, _lensFacing);

			StartRecording(file, flashMode, size.Width, size.Height, rotation);
		}

		//start recording video
		public void StartRecording(string file, FlashMode flashMode, int width, int heigth, int rotation)
		{
			if (mMediaRecorder != null)
				return;

			MediaRecorder recorder = new MediaRecorder();

			recorder.SetVideoSource(VideoSource.Surface);
			recorder.SetAudioSource(AudioSource.Mic);
			recorder.SetOutputFormat(OutputFormat.Mpeg4);
			recorder.SetOutputFile(file);
			recorder.SetVideoEncodingBitRate(6000000);
			recorder.SetVideoFrameRate(30);
			recorder.SetVideoSize(width, heigth);
			recorder.SetVideoEncoder(VideoEncoder.H264);
			recorder.SetAudioEncoder(AudioEncoder.Aac);

			recorder.SetOrientationHint(rotation);
			recorder.Prepare();

			List<Surface> surfaces = new List<Surface>();
			surfaces.Add( recorder.Surface );

			if (_TextureView != null)
				surfaces.Add(getPreviewSurface());

			createCapture(surfaces, flashMode, true, mDevice.CreateCaptureRequest(CameraTemplate.Record));
			recorder.Start();
			mMediaRecorder = recorder;
			mMediaFile = file;
		}

		//stop recording video
		public void StopRecording()
		{
			if (mMediaRecorder == null)
			  return;

			lock( mMediaRecorder )
			{
				if (mMediaRecorder == null)
			     return;

				try
				{
					mMediaRecorder.Stop();
					mMediaRecorder = null;
				}
				catch (System.Exception e)
				{
					mMediaRecorder = null;
					throw e;				    
				}
			}

			OnVideoCaptured(mMediaFile);

			startPreview();
		}

		//get the supported sizes for images
		public Size[] GetImageSizes()
		{
			return getSizesSupported(Format.Jpeg);
		}

		//Take picture using the same aspect ratio as the preview
		public void TakePicture( string file, FlashMode flashMode )
		{

			int width, height;

			if (mRotate)
			{
				width = _TextureView.Height;
				height = _TextureView.Width;
			}
			else
			{
				width = _TextureView.Width;
				height = _TextureView.Height;
			}

			var sizes = getSizesSupported(Format.Jpeg);
			var size = getBestSize(sizes, width, height);

            var c = mCameraManager.GetCameraCharacteristics(_id);
			int rotation = (int)c.Get(CameraCharacteristics.SensorOrientation);

			//if (_lensFacing == LensFacing.Front)
			//	rotation *= -1;

			rotation = getJpegOrientation(rotation, 0, _lensFacing);

			TakePicture(file, flashMode, size.Width, size.Height, rotation );

		}

		//Take a picture
        public void TakePicture(string file, FlashMode flashMode, int width, int height, int rotation)
		{
			ImageReader imageReader = ImageReader.NewInstance( width, height, ImageFormatType.Jpeg, 1 );
			imageReader.SetOnImageAvailableListener(this, mHandler);

			//The surface for the image
			var imageSurface = imageReader.Surface;

			List<Surface> surfaces = new List<Surface>();
			surfaces.Add(imageSurface);

			//the surface for the preview
			if (_TextureView != null)
			{ 
               var previewSurface = getPreviewSurface();
				surfaces.Add(previewSurface);
			}

			var builder = mDevice.CreateCaptureRequest(CameraTemplate.StillCapture);
			builder.Set(CaptureRequest.JpegOrientation, rotation);

			mMediaFile = file;
			createCapture(surfaces, flashMode, false, builder );

		}

		//WHen the picture is avilable
		void ImageReader.IOnImageAvailableListener.OnImageAvailable(ImageReader reader)
		{
			saveImage(reader.AcquireLatestImage());       
		}

		private void saveImage( Image image )
		{
			Task.Run( ()=> { 
			
				try
				{
					//image.CropRect

					var plane = image.GetPlanes()[0];
					var buffer = plane.Buffer;

					byte[] byteBuffer = new byte[buffer.Capacity()];
					buffer.Get(byteBuffer);

					File.WriteAllBytes(mMediaFile, byteBuffer);

                    OnImageCaptured(mMediaFile);
				
				}
				catch (System.Exception e)
				{
					OnImageCaptureError(e);
				}

				//startPreview();
			} );


		}

        private int getJpegOrientation(int sensorOrientation, int deviceOrientation, LensFacing lensFacing)
		{
			// Round device orientation to a multiple of 90
			deviceOrientation = (deviceOrientation + 45) / 90 * 90;

			// Reverse device orientation for front-facing cameras
			if (lensFacing == LensFacing.Front)
				deviceOrientation = -deviceOrientation;

			// Calculate desired JPEG orientation relative to camera orientation to make
			// the image upright relative to the device orientation
			int jpegOrientation = (sensorOrientation + deviceOrientation + 360) % 360;

			return jpegOrientation;
		}

		//init instance with preview
		public ACamera(Context context, LensFacing lensFacing, Handler handler, TextureView preview)
		{
			initInstance(context, lensFacing, handler);
			TextureView = preview;
		}

		//Basic constructor
        public ACamera( Context context, LensFacing lensFacing, Handler handler ){initInstance(context, lensFacing, handler);}

		//Initialize the instance of the object
		private void initInstance(Context context, LensFacing lensFacing, Handler handler)
		{ 
		    //mContext = context;
			mHandler = handler;
			_lensFacing = lensFacing;

			mCameraStateCallBack = new CameraStateCallBack(this);
			mCaptureSessionStateCallBack = new CaptureSessionStateCallBack(this, false);
			mSingleCaptureSessionStateCallBack = new CaptureSessionStateCallBack(this, true);

			//get the Camera Manager
			var manager = (CameraManager)context.GetSystemService(Context.CameraService);
			mCameraManager = manager;

			//open the camera
			openCamera();
		}

		//open the camera device
		private void openCamera()
		{
			closeCamera();

            //get the id
            var id = getId(mCameraManager, _lensFacing);

			var c = mCameraManager.GetCameraCharacteristics(id);
			int rotation = (int)c.Get(CameraCharacteristics.SensorOrientation);

			if (rotation == 90 || rotation == 270)
				mRotate = true;
			else
				mRotate = false;

			mCameraManager.OpenCamera(id, mCameraStateCallBack, mHandler);
			_id = id;
		}

		//close the camera
		private void closeCamera()
		{
			if (mDevice == null)
				return;

			mDevice.Close();
			if ( mCaptureBuilder != null )
			mCaptureBuilder.Dispose();
			mDevice = null;
			mCaptureBuilder = null;
		}

		//funtion to obtain the camera id with the lensFacing
		private static string getId( CameraManager manager, LensFacing lensFacing)
		{
			//Obtain all of the disponibles ids
			var ids = manager.GetCameraIdList();

			//verify all cameras characteristics
			foreach (var id in ids)
			{
				//get the characteristics
				var c = manager.GetCameraCharacteristics(id);

				//if the camera has the same lens facing return it
				if ((int)c.Get(CameraCharacteristics.LensFacing) == (int)lensFacing)
					return id;

			}
			//return this when can't find any camera
			return "NotSupported";
		}

		//get the best size
		private static Size getBestSize(Size[] sizes, int width, int heigth)
		{
			//the ratio of the target
			double ratio = (double)width / (double)heigth;

			Size result = sizes[0];
			//min difference
			double minDiff = double.PositiveInfinity;

			foreach (var size in sizes)
			{
				//ratio of the sample
				double sizeRatio = (double)size.Width / (double)size.Height;
				//the difference
				double diff = System.Math.Abs(sizeRatio - ratio);

				//This is the best value for now
				if (diff < minDiff)
				{
					minDiff = diff;
					result = size;
				}
			}

			return result;
		}

		//get the supported sizes based on the format
		private Size[] getSizesSupported( Format format )
		{
			var c = mCameraManager.GetCameraCharacteristics(_id);
            var map = (StreamConfigurationMap)c.Get(CameraCharacteristics.ScalerStreamConfigurationMap);

			return map.GetOutputSizes( (int)format );
		}

		//get the supported sizes based on the output type
        private Size[] getSizesSupported(Type type)
		{
			var c = mCameraManager.GetCameraCharacteristics(_id);
			var map = (StreamConfigurationMap)c.Get(CameraCharacteristics.ScalerStreamConfigurationMap);
            
			return map.GetOutputSizes( Java.Lang.Class.FromType(type) );
		}

		//get the surface for the preview
		private Surface getPreviewSurface()
		{
			if (_TextureView == null)
				return null;

			var txSurface = _TextureView.SurfaceTexture;

			//if ( mRotate )
			//  txSurface.SetDefaultBufferSize(mPreviewSize.Height, mPreviewSize.Width);
			//else
			  txSurface.SetDefaultBufferSize(mPreviewWidth, mPreviewHeight);

			return new Surface(txSurface);
		}

		//adjust the aspect ratio of the texture View in order to prevent preview distorcion
		private void adjustAspectRatio(int pWidth, int pHeight)
		{
			if (_TextureView == null || !_TextureView.IsAvailable)
				return;

			var width = _TextureView.Width;
			var height = _TextureView.Height;
			double ratio = (double)pHeight / (double)pWidth;

			//the new width and heigth of the SurfaceTexture
			int newWidth, newHeight;

			if ((double)width > (double)((double)width * ratio))
			{
				// limited by narrow width; restrict height
				newWidth = width;
				newHeight = (int)(width * ratio);
			}
			else
			{
				// limited by short height; restrict width
				newWidth = (int)(height / ratio);
				newHeight = height;
			}

			int xoff = (width - newWidth) / 2;
			int yoff = (height - newHeight) / 2;

			Matrix txform = new Matrix();
			_TextureView.GetTransform(txform);
			txform.SetScale((float)newWidth / (float)width, (float)newHeight / (float)height);
			txform.PostTranslate(xoff, yoff);
			_TextureView.SetTransform(txform);

		}

		//return true if the camera support flash
		public bool SupportFlash()
		{
			var c = mCameraManager.GetCameraCharacteristics(_id);
			return (bool)c.Get(CameraCharacteristics.FlashInfoAvailable);
		}

		//create a repeat capture
		private void createCapture( List<Surface> surfaces, FlashMode flashMode, bool repeat, CaptureRequest.Builder builder )
		{
			if (mDevice == null)
				return;
			
			//Config the flash
			if (SupportFlash())
				builder.Set(CaptureRequest.FlashMode, (int)flashMode);

			//Set the zoom
			builder.Set(CaptureRequest.ScalerCropRegion, getRectZoom( mCameraManager, mZoom, _id ) );

			//Set party mode
			builder.Set(CaptureRequest.ControlSceneMode, (int)ControlSceneMode.Hdr);

			//Add the targets to the builder
			foreach (var surface in surfaces)
				builder.AddTarget(surface);

			mCaptureBuilder = builder;
			if (repeat)
				mDevice.CreateCaptureSession(surfaces, mCaptureSessionStateCallBack, mHandler);
			else
				mDevice.CreateCaptureSession(surfaces, mSingleCaptureSessionStateCallBack, mHandler);	
		}

		//only use the preview
		private void startPreview()
		{
			var surface = getPreviewSurface();
			createCapture(new List<Surface>() { surface }, FlashMode.Off, true, mDevice.CreateCaptureRequest(CameraTemplate.Preview) );
		}

		//get the Rectangle that correspond with the zoom
		private static Rect getRectZoom( CameraManager manager, float zoom, string cameraId)
		{
			var c = manager.GetCameraCharacteristics(cameraId);

			var maxZoom = (Rect)c.Get(CameraCharacteristics.SensorInfoActiveArraySize);
			float reverse = 100f - zoom;

			int width = System.Math.Abs( maxZoom.Width() );
			int heigth = System.Math.Abs( maxZoom.Height() );

			//Calc the width
			int newWidth = (int)(((float)width * reverse) / 100f);
			//Calc the heigth
			int newHeight = (int)(((float)heigth * reverse) / 100f);

			//Calculate the positions
			int xOff = (width - newWidth)/2;
			int yOff = (heigth - newHeight)/2;

			return new Rect(xOff, yOff, xOff + newWidth, yOff + newHeight);
		}

		//Camera Disconnected event caller
		private void OnCameraDisconnected()
		{
			if (CameraDisconnected != null)
				CameraDisconnected(this, new EventArgs());
		}

		// Camera Error event caller
		private void OnCameraError( CameraError error )
		{
			if (CameraError != null)
				CameraError(this, new CameraErrorEventArgs(error));
		}

		// Image Captured event caller
		private void OnImageCaptured( string file )
		{
			if (ImageCaptured != null)
				ImageCaptured(this, new ImageCapturedEventArgs(file));
		}

		//Image Capture error event caller
		private void OnImageCaptureError(System.Exception e)
		{
			if (ImageCaptureError != null)
				ImageCaptureError(this, new ImageCaptureErrorEventArgs(e));
		}

		// Image Captured event caller
		private void OnVideoCaptured(string file)
		{
			if (VideoCaptured != null)
				VideoCaptured(this, new ImageCapturedEventArgs(file));
		}


		//Camera error eventArgs
		public class CameraErrorEventArgs : EventArgs
		{
			private CameraError _error;
			public CameraError Error { get { return _error; } }

			public CameraErrorEventArgs(CameraError error)
			{
				_error = error;
			}
		}
		//Image Captured Events Args
		public class ImageCapturedEventArgs : EventArgs
		{
			private string _file;
			public string File
			{ 
				get {
					return _file;
				}
			}

			public ImageCapturedEventArgs(string file)
			{
				_file = file;
			}
		}
		//Image Captured Events Args
		public class ImageCaptureErrorEventArgs : EventArgs
		{
			private System.Exception _e;
			public System.Exception Exception
			{
				get
				{
					return _e;
				}
			}

			public ImageCaptureErrorEventArgs(System.Exception e)
			{
				_e = e;
			}
		}


		//Texture View Listener-------------------
		void TextureView.ISurfaceTextureListener.OnSurfaceTextureAvailable(SurfaceTexture surface, int width, int height)
		{
			if (mRotate)
			{
				mPreviewSize = getBestSize(getSizesSupported(typeof(SurfaceTexture)), _TextureView.Height, _TextureView.Width);
				mPreviewWidth = mPreviewSize.Width;
				mPreviewHeight = mPreviewSize.Height;

				adjustAspectRatio(mPreviewSize.Height, mPreviewSize.Width);
			}
			else
			{ 
			    mPreviewSize = getBestSize(getSizesSupported(typeof(SurfaceTexture)), _TextureView.Width, _TextureView.Height);
				mPreviewWidth = mPreviewSize.Width;
				mPreviewHeight = mPreviewSize.Height;

				adjustAspectRatio(mPreviewSize.Width, mPreviewSize.Height);
			}

			if ( mDevice != null )
			 startPreview();
		}

		bool TextureView.ISurfaceTextureListener.OnSurfaceTextureDestroyed(SurfaceTexture surface)
		{
			return true;
		}

		void TextureView.ISurfaceTextureListener.OnSurfaceTextureSizeChanged(SurfaceTexture surface, int width, int height)
		{

		}

		void TextureView.ISurfaceTextureListener.OnSurfaceTextureUpdated(SurfaceTexture surface)
		{

		}

	    //Camera State CallBack
	    private class CameraStateCallBack : CameraDevice.StateCallback
		{
			ACamera mP;
			public CameraStateCallBack(ACamera p)
			{
				mP = p;
			}

			public override void OnDisconnected(CameraDevice camera)
			{
				mP.OnCameraDisconnected();
			}

			public override void OnError(CameraDevice camera, [GeneratedEnum] CameraError error)
			{
				mP.OnCameraError(error);
			}

			public override void OnOpened(CameraDevice camera)
			{
				mP.mDevice = camera;
				if (mP._TextureView != null && mP._TextureView.IsAvailable)
				{
					if (mP.mRotate)
					{
						mP.mPreviewSize = getBestSize(mP.getSizesSupported(typeof(SurfaceTexture)), mP._TextureView.Height, mP._TextureView.Width);
						mP.mPreviewWidth = mP.mPreviewSize.Width;
				        mP.mPreviewHeight = mP.mPreviewSize.Height;
						mP.adjustAspectRatio(mP.mPreviewSize.Height, mP.mPreviewSize.Width);
					}
					else
					{
						mP.mPreviewSize = getBestSize(mP.getSizesSupported(typeof(SurfaceTexture)), mP._TextureView.Width, mP._TextureView.Height);
						mP.mPreviewWidth = mP.mPreviewSize.Width;
				        mP.mPreviewHeight = mP.mPreviewSize.Height;
						mP.adjustAspectRatio(mP.mPreviewSize.Width, mP.mPreviewSize.Height);
					}
				   mP.startPreview();
				}
			}
		}

		//The capture session state callback
		private class CaptureSessionStateCallBack : CameraCaptureSession.StateCallback
		{
            ACamera mP;
			bool mSingleCapture;
			CaptureCallBack callBack;

			public CaptureSessionStateCallBack(ACamera p, bool singleCapture)
			{
				mP = p;
				mSingleCapture = singleCapture;

				if (!singleCapture)
					callBack = new CaptureCallBack(mP);

			}

			public override void OnConfigured(CameraCaptureSession session)
			{
				try
				{
					if (!mSingleCapture)
						//session.Capture(mP.mCaptureBuilder.Build(), callBack, mP.mHandler);
						session.SetRepeatingRequest( mP.mCaptureBuilder.Build(), callBack, mP.mHandler);
					else 
						session.Capture(mP.mCaptureBuilder.Build(), null, mP.mHandler);
				}
				catch
				{
					return;
				}
			}

			public override void OnConfigureFailed(CameraCaptureSession session)
			{
				//throw new NotImplementedException();
				//ignore
			}

			private class CaptureCallBack : CameraCaptureSession.CaptureCallback
			{ 
                ACamera mP;
				public CaptureCallBack(ACamera p)
				{
					mP = p;
				}

				public override void OnCaptureStarted(CameraCaptureSession session, CaptureRequest request, long timestamp, long frameNumber)
				{
					base.OnCaptureStarted(session, request, timestamp, frameNumber);

					try
					{
						session.SetRepeatingRequest(mP.mCaptureBuilder.Build() , this, mP.mHandler);
                    }
					catch( System.Exception e )
					{
						Log.Debug("ACamera", "SessionError: {0}", e.Message);
					}

				}


			}

		}



	}
}
