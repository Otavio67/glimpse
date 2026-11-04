# Baseline: synthetic BGRA → FFmpeg/Media Foundation H.264

Recorded on Windows 11 Pro 10.0.26200, Intel Core i5-7600K (4 logical processors), NVIDIA GeForce GTX 1060 6 GB, driver 560.94. FFmpeg 9.0.2 Essentials, .NET SDK 10.0.403, `h264_mf` hardware encoding, output 1280×720 at 30 fps and 2.5 Mb/s.

This was a 120-second **synthetic encoder-only** test. A preallocated artificial BGRA image was supplied from CPU memory. It does not capture a user's display or window and does **not** exercise WGC GPU readback, WebRTC, receiving, or real content changes. It is not a measurement of the complete app's CPU or capture pipeline, and does not establish that either CPU target is met. Another test with the same input must be used for a meaningful encoder-only comparison.

- 3,574 frames accepted, 26 frame submissions dropped, 3,573 encoded access units. Actual elapsed time 120.07 s; hardware encoder selected; no codec error reported.
- Across 100 samples, average benchmark-process CPU was 0.81% of total machine capacity, and average FFmpeg CPU was 3.81%. Combined process CPU average was approximately 4.62%. Peak values for the processes individually were 3.64% and 7.60%, respectively; the peaks did not necessarily occur at the same instant.
- Average working sets: 49.93 MB (benchmark process) and 62.76 MB (FFmpeg). This is not whole-system memory or the application's normal working set.
- NVIDIA device-wide utilization averaged 11.69% GPU and 6.16% encoder. These are whole-device samples and can include other applications. GPU memory averaged 1710 MB device-wide, not allocated solely to Glimpse. Sampling intervals vary slightly because the GPU and process counters are queried in each iteration.

The first sampling script wrote decimals using the Windows locale, so the CSV was converted to invariant decimal notation after collection without changing the numbers. Its PowerShell exit-code handling reported failure after the process had already written its successful final summary; the script has since been corrected. Raw samples: `baseline-synthetic-720p30.csv`; process summary: `baseline-synthetic-720p30.log`. No screen capture was performed. No after measurement exists yet.
