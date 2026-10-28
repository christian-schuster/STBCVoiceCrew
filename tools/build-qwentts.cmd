@echo off
rem Builds qwentts.cpp (Qwen3-TTS, MIT) with CUDA for the speech output of the optional LLM mode.
rem Requirements: Visual Studio 2026 with C++, Windows SDK, CMake, CUDA Toolkit 13.4.
rem Usage: build-qwentts.cmd <qwentts.cpp source folder> <short build folder, e.g. C:\qtc>
rem Notes:
rem  - CUDA 12.x does not work with Visual Studio 2026 (cudafe++ crashes), hence CUDA 13.4.
rem  - CMAKE_DEPENDS_USE_COMPILER=OFF works around a CMake 3.29 bug with NMake + CUDA depfiles.
rem  - Architecture 120 = RTX 50 (Blackwell). For older cards use e.g. 86 (RTX 30) or 89 (RTX 40).
set SRC=%~1
set OUT=%~2
set CU=C:\Program Files\NVIDIA GPU Computing Toolkit\CUDA\v13.4
call "C:\Program Files\Microsoft Visual Studio\18\Community\VC\Auxiliary\Build\vcvars64.bat" >nul
set "CUDA_PATH=%CU%"
set "PATH=%CU%\bin;%PATH%"
if not exist "%OUT%" mkdir "%OUT%"
cmake -S "%SRC%" -B "%OUT%" -G "NMake Makefiles" -DCMAKE_BUILD_TYPE=Release -DCMAKE_DEPENDS_USE_COMPILER=OFF -DGGML_CUDA=ON -DCMAKE_CUDA_ARCHITECTURES=120 "-DCMAKE_CUDA_COMPILER=%CU%\bin\nvcc.exe" "-DCUDAToolkit_ROOT=%CU%" || exit /b 1
cmake --build "%OUT%" || exit /b 1
echo Fertig. Nach BepInEx\plugins\StbcAiCrew\qwentts kopieren: tts-server.exe, qwen-tts.exe, qwen-codec.exe, ggml*.dll
echo sowie cudart64_13.dll, cublas64_13.dll, cublasLt64_13.dll (z. B. aus dem llama.cpp-CUDA-13.4-Paket).
