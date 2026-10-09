#pragma once

// miniaudio (Lib/3rdParty/miniaudio), without the parts the engine does not use. Include this
// header instead of miniaudio.h so every file sees the same configuration.
#define MA_NO_ENCODING
#define MA_NO_FLAC

#include <miniaudio.h>
