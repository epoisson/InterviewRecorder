# Third-party notices

This project uses the following third-party components. Their licenses apply to
those components only; this project's own code is released under the Unlicense
(see LICENSE).

## NAudio

Audio capture, playback, and WAV handling.

- Project: https://github.com/naudio/NAudio
- License: MIT

```
MIT License

Copyright (c) Mark Heath and Contributors

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

## .NET and WPF

Runtime and UI framework (Microsoft).

- Project: https://github.com/dotnet
- License: MIT

## FFmpeg

Used as an external command-line program for audio compression and merging. It
is invoked at runtime via the configured `FFmpegPath` and is not distributed
with this project. Install FFmpeg separately.

- Project: https://ffmpeg.org
- License: LGPL-2.1 or GPL, depending on the build you install

If you choose to bundle an FFmpeg binary with a distribution of this software,
you must comply with that binary's license terms.
