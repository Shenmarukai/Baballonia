{
  description = "libuvc-based capture plugin for Baballonia - BSB2E support";

  inputs.nixpkgs.url = "github:NixOS/nixpkgs/nixos-unstable";

  outputs = { self, nixpkgs }: let
    systems = ["x86_64-linux" "aarch64-linux"];
    forAllSystems = nixpkgs.lib.genAttrs systems;
    pkgsFor = system: import nixpkgs { inherit system; };
  in {
    packages = forAllSystems (system: let
      pkgs = pkgsFor system;
      dotnet = pkgs.dotnetCorePackages.dotnet_8;
    in {
      default = pkgs.buildDotnetModule {
        pname = "baballonia-libuvc-capture";
        version = "0.1.0";

        src = ./.;

        projectFile = "Baballonia.LibuvcCapture.csproj";
        nugetDeps = ./deps.json;

        dotnetSdk = dotnet.sdk;
        dotnetRuntime = dotnet.runtime;

        buildInputs = with pkgs; [
          libuvc
          libusb1
          opencv
        ];

        meta = with pkgs.lib; {
          description = "libuvc-based capture plugin for Bigscreen Beyond 2E eye tracking";
          license = licenses.lgpl3Plus;
          platforms = platforms.linux;
        };
      };
    });

    devShells = forAllSystems (system: let
      pkgs = pkgsFor system;
      dotnet = pkgs.dotnetCorePackages.dotnet_8;
    in {
      default = pkgs.mkShell {
        packages = with pkgs; [
          dotnet.sdk
          libuvc
          libusb1
          opencv
        ];

        shellHook = ''
          export DOTNET_ROOT="${dotnet.sdk}"
        '';
      };
    });
  };
}
