@echo off
rem Baut qwentts.cpp (Qwen3-TTS, MIT) mit CUDA fuer die Sprachausgabe des Mods.
rem Voraussetzungen: Visual Studio 2026 mit C++, Windows-SDK, CMake, CUDA Toolkit 13.4.
rem Aufruf: build-qwentts.cmd <Quellordner qwentts.cpp> <kurzer Build-Ordner, z. B. G:\qtc>
rem Hinweise:
rem  - CUDA 12.x passt nicht zu Visual Studio 2026 (cudafe++ stuerzt ab), daher CUDA 13.4.
rem  - CMAKE_DEPENDS_USE_COMPILER=OFF umgeht einen Fehler von CMake 3.29 mit NMake + CUDA-Depfiles.
rem  - Architektur 120 = RTX 50 (Blackwell). Fuer aeltere Karten z. B. 86 (RTX 30) oder 89 (RTX 40) setzen.
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
