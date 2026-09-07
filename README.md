# fort.desktop
fort.ind, but on windows! with all the features you want (and profiles!), made with winUI + C# ❤️ 

<img width="1311" height="826" alt="image" src="https://github.com/user-attachments/assets/c698b6b0-2d43-4ccc-a70f-96fbaa25c35e" />

# requirements
Must be at least on Windows 10 version 1809; the dependencies for the app are in the artifacts folder 
# installing
If you want a stable experience, get the latest [release](https://github.com/fort-ind/uwp/releases/latest), or if you like seeing what we are cooking and are okay with rough edges, go to the Actions tab and grab it from there (or use our [nightly.link](https://nightly.link/fort-ind/uwp/workflows/build-msix/master))
### the included PS script (easiest :3)
Just run as admin, and you're good to go!
### installing using the appx and cer 
first install the .cer file to your local machine (otherwise it won't work 3:) click browse and select **trusted people**, NOT trusted root certificates authorities. Click next and Finish, and after that turn on developer mode in Windows. Then run the APPX file and click Install 
> [!NOTE]
> If the script doesnt run (ps crashes right when you open it), right-click install.ps1 > properties > unblock file
# buliding
its strongly recommended to build this app on windows 10 21H2, the easiest way is to open the .sln file in visual studio and click "build solution" or just click the green play button to actually see the app 
OR just run this :) dosent make the 
```bash
msbuild "Fort.ind UWP\Fort.ind UWP.csproj" /r /p:AppxPackageSigningEnabled=false /p:GenerateAppxPackageOnBuild=false
```
for some reason those flags are needed otherwise it explodes :( 
