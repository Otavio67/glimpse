// Experimental standalone probe only. Not used by the shipping capture/encoder pipeline.
using Vortice.MediaFoundation;
using System.Runtime.InteropServices;
using SharpGen.Runtime;
using Vortice.Direct3D11;
using Vortice.Direct3D;
using Vortice.Mathematics;
using Vortice.DXGI;
using static Vortice.MediaFoundation.MediaFactory;
MFStartup().CheckError();
try {
 var cat=new Guid("f79eac7d-e545-4387-bdee-d647d7bde42a"); var video=new Guid("73646976-0000-0010-8000-00aa00389b71");
 var format=new RegisterTypeInfo {GuidMajorType=video,GuidSubtype=VideoFormatGuids.H264};
 using var collection=MFTEnumEx(cat, 6, null, format);
 using var device=D3D11.D3D11CreateDevice(DriverType.Hardware,DeviceCreationFlags.BgraSupport|DeviceCreationFlags.VideoSupport,FeatureLevel.Level_11_0);
 using var manager=MFCreateDXGIDeviceManager(); manager.ResetDevice(device).CheckError();
 int encoderCount=0;
 foreach (var activation in collection) {
  encoderCount++;
  try {
   Console.WriteLine("encoder="+activation.GetString(TransformAttributeKeys.MftFriendlyNameAttribute));

   int submitted=0, produced=0, returned=0; using var callback=new TrackedCallback(()=>Interlocked.Increment(ref returned));
   using var encoder=activation.ActivateObject<IMFTransform>(); using var attr=encoder.Attributes;
   Console.WriteLine("async="+attr.GetUInt32(TransformAttributeKeys.TransformAsync)); attr.Set(TransformAttributeKeys.TransformAsyncUnlock,1u).CheckError();
   encoder.ProcessMessage(TMessageType.MessageSetD3DManager,(nuint)manager.NativePointer);
   using var events=encoder.QueryInterface<IMFMediaEventGenerator>();
   using var output=MFCreateMediaType();
   output.Set(MediaTypeAttributeKeys.MajorType,video).CheckError(); output.Set(MediaTypeAttributeKeys.Subtype,VideoFormatGuids.H264).CheckError();
   output.Set(MediaTypeAttributeKeys.AvgBitrate,2500000u).CheckError(); output.Set(MediaTypeAttributeKeys.InterlaceMode,2u).CheckError();
   output.Set(MediaTypeAttributeKeys.FrameSize,PackSize(1280,720)).CheckError(); output.Set(MediaTypeAttributeKeys.FrameRate,PackRatio(30,1)).CheckError();
   output.Set(MediaTypeAttributeKeys.PixelAspectRatio,PackRatio(1,1)).CheckError();
   encoder.SetOutputType(0,output,0); Console.WriteLine("output set");
   using var input=MFCreateMediaType(); input.Set(MediaTypeAttributeKeys.MajorType,video).CheckError(); input.Set(MediaTypeAttributeKeys.Subtype,VideoFormatGuids.NV12).CheckError();
   input.Set(MediaTypeAttributeKeys.InterlaceMode,2u).CheckError(); input.Set(MediaTypeAttributeKeys.FrameSize,PackSize(1280,720)).CheckError(); input.Set(MediaTypeAttributeKeys.FrameRate,PackRatio(30,1)).CheckError(); input.Set(MediaTypeAttributeKeys.PixelAspectRatio,PackRatio(1,1)).CheckError();
   encoder.SetInputType(0,input,0); Console.WriteLine("input set, stream info: "+encoder.GetOutputStreamInfo(0).Flags);
   encoder.ProcessMessage(TMessageType.MessageNotifyBeginStreaming,0); encoder.ProcessMessage(TMessageType.MessageNotifyStartOfStream,0);
   var desc=new Texture2DDescription { Width=1280, Height=720, MipLevels=1, ArraySize=1, Format=Format.NV12, SampleDescription=new SampleDescription(1,0), Usage=ResourceUsage.Default, BindFlags=BindFlags.RenderTarget|BindFlags.ShaderResource };
   using var texture=device.CreateTexture2D(desc);
   using var vdevice=device.QueryInterface<ID3D11VideoDevice>(); using var vcontext=device.ImmediateContext.QueryInterface<ID3D11VideoContext>();
   var videoDesc=new VideoProcessorContentDescription {InputFrameFormat=VideoFrameFormat.Progressive,InputWidth=1280,InputHeight=720,OutputWidth=1280,OutputHeight=720,InputFrameRate=new Rational(30,1),OutputFrameRate=new Rational(30,1),Usage=VideoUsage.PlaybackNormal};
   using var enumerator=vdevice.CreateVideoProcessorEnumerator(videoDesc); using var videoProcessor=vdevice.CreateVideoProcessor(enumerator,0);
   var bgraDesc=new Texture2DDescription { Width=1280,Height=720,MipLevels=1,ArraySize=1,Format=Format.B8G8R8A8_UNorm,SampleDescription=new SampleDescription(1,0),Usage=ResourceUsage.Default,BindFlags=BindFlags.RenderTarget|BindFlags.ShaderResource };
   using var inputTexture=device.CreateTexture2D(bgraDesc); using var renderView=device.CreateRenderTargetView(inputTexture); device.ImmediateContext.ClearRenderTargetView(renderView,new Color4(0.3f,0.1f,0.7f,1f));
   using var inputView=vdevice.CreateVideoProcessorInputView(inputTexture,enumerator,new VideoProcessorInputViewDescription {ViewDimension=VideoProcessorInputViewDimension.Texture2D});
   using var outputView=vdevice.CreateVideoProcessorOutputView(texture,enumerator,new VideoProcessorOutputViewDescription {ViewDimension=VideoProcessorOutputViewDimension.Texture2D});
   vcontext.VideoProcessorBlt(videoProcessor,outputView,0,new[]{new VideoProcessorStream{Enable=true,InputSurface=inputView}}).CheckError();
   Console.WriteLine("GPU BGRA to NV12 conversion completed");

   while(produced<5 && submitted<20) {
     using var evt=events.GetEvent(0);
     if(evt.EventType==MediaEventTypes.TransformNeedInput) {
       MFCreateTrackedSample(out var tracked).CheckError(); using var trackedOwner=tracked; tracked.SetAllocator(callback,null!); using var sample=tracked.QueryInterface<IMFSample>(); using var buffer=MFCreateDXGISurfaceBuffer(typeof(ID3D11Texture2D).GUID,texture,0,false); sample.AddBuffer(buffer); sample.SampleTime=submitted*333333L; sample.SampleDuration=333333;
       encoder.ProcessInput(0,sample,0); submitted++;
     } else if(evt.EventType==MediaEventTypes.TransformHaveOutput) {
       var data=new OutputDataBuffer {StreamID=0}; ProcessOutputStatus status; var result=encoder.ProcessOutput(ProcessOutputFlags.None,1,ref data,out status); result.CheckError();
       if(data.Sample is not null){using(var b=data.Sample.ConvertToContiguousBuffer()){ b.Lock(out var ptr,out var max,out var len); try {var raw=new byte[Math.Min(len,24)];Marshal.Copy(ptr,raw,0,raw.Length);Console.WriteLine("encoded bytes="+len+" prefix="+Convert.ToHexString(raw));} finally{b.Unlock();}}data.Sample.Dispose();produced++;} data.Events?.Dispose();
     }
   }
   await Task.Delay(200); Console.WriteLine($"GPU textures submitted={submitted}; outputs={produced}; released callbacks={returned}"); if (produced<5 || returned==0) throw new InvalidOperationException("GPU encoder did not return the expected samples.");
  }catch(Exception ex){Console.WriteLine("error="+ex); throw;}
 }
 if(encoderCount==0) throw new InvalidOperationException("Nenhum codificador H.264 de hardware encontrado.");
}finally{MFShutdown();}




sealed class TrackedCallback(Action release) : CallbackBase, IMFAsyncCallback {
 public Result GetParameters(out AsyncCallbackFlags flags,out int queue) {flags=default;queue=0;return new Result(unchecked((int)0x80004001));}
 public Result Invoke(IMFAsyncResult result) {release();return Result.Ok;}
}
