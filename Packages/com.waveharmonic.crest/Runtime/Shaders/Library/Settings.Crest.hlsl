// Crest Water System
// Copyright © 2024 Wave Harmonic. All rights reserved.

#ifndef d_WaveHarmonic_Crest_Settings
#define d_WaveHarmonic_Crest_Settings

#include "Packages/com.waveharmonic.crest/Runtime/Shaders/Library/Settings/Settings.Crest.hlsl"

#if   CREST_PLATFORM_STANDALONE
#include "Packages/com.waveharmonic.crest/Runtime/Shaders/Library/Settings/Settings.Crest.Standalone.hlsl"
#elif CREST_PLATFORM_SERVER
#include "Packages/com.waveharmonic.crest/Runtime/Shaders/Library/Settings/Settings.Crest.Server.hlsl"
#elif CREST_PLATFORM_ANDROID
#include "Packages/com.waveharmonic.crest/Runtime/Shaders/Library/Settings/Settings.Crest.Android.hlsl"
#elif CREST_PLATFORM_IOS
#include "Packages/com.waveharmonic.crest/Runtime/Shaders/Library/Settings/Settings.Crest.iOS.hlsl"
#elif CREST_PLATFORM_WEB
#include "Packages/com.waveharmonic.crest/Runtime/Shaders/Library/Settings/Settings.Crest.Web.hlsl"
#elif CREST_PLATFORM_TVOS
#include "Packages/com.waveharmonic.crest/Runtime/Shaders/Library/Settings/Settings.Crest.tvOS.hlsl"
#elif CREST_PLATFORM_VISIONOS
#include "Packages/com.waveharmonic.crest/Runtime/Shaders/Library/Settings/Settings.Crest.visionOS.hlsl"
#else
#include "Packages/com.waveharmonic.crest/Runtime/Shaders/Library/Settings/Settings.Crest.Default.hlsl"
#endif

#endif // d_WaveHarmonic_Crest_Settings
