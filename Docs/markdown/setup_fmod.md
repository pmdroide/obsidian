# Setup Instructions

Because FMOD cannot be bundled with this engine out of the box, you must manually add the FMOD SDK to your local environment to enable audio functionality.

## Step 1: Download the FMOD Engine SDK
1. Head over to the official [FMOD Downloads Page](https://www.fmod.com/download).
2. Create a free account (required by Firelight Technologies to access the SDK).
3. Under the **FMOD Engine** section, download the **Programmer API** package that matches your development operating system (Windows, macOS, or Linux).
   > *Note: Please ensure you download version `2.02.25` to match this integration.*

## Step 2: Install the Binaries into the Engine
Once downloaded, extract the SDK and copy the required files into the engine's third-party directory.

1. Locate the `thirdparty/fmod/` folder in this engine's directory.
2. Copy the contents of the FMOD SDK into it following this structure:

```text
your-engine-root/
└── thirdparty/
    └── fmod/
        ├── include/           <-- Place FMOD header files here (.h, .hpp)
        └── lib/
            ├── win64/         <-- Place Windows .dll and .lib files here
            ├── osx/           <-- Place macOS .dylib files here
            └── linux64/       <-- Place Linux .so files here
```