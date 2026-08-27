import AppKit
import Foundation

func fail(_ message: String, code: Int32 = 1) -> Never {
    if let data = (message + "\n").data(using: .utf8) {
        FileHandle.standardError.write(data)
    }
    exit(code)
}

guard CommandLine.arguments.count == 3, CommandLine.arguments[1] == "set-wallpaper" else {
    fail("Usage: EarthWallpaperMacHelper set-wallpaper <absolute-image-path>", code: 64)
}

let imagePath = CommandLine.arguments[2]
guard FileManager.default.fileExists(atPath: imagePath) else {
    fail("The source image could not be found.", code: 66)
}

let imageUrl = URL(fileURLWithPath: imagePath, isDirectory: false)
let workspace = NSWorkspace.shared
let screens = NSScreen.screens
guard !screens.isEmpty else {
    fail("macOS did not report any connected displays.")
}

var failures: [String] = []
for (index, screen) in screens.enumerated() {
    do {
        let options = workspace.desktopImageOptions(for: screen) ?? [:]
        try workspace.setDesktopImageURL(imageUrl, for: screen, options: options)
    } catch {
        failures.append("display \(index + 1): \(error.localizedDescription)")
    }
}

if !failures.isEmpty {
    fail("Could not update " + failures.joined(separator: "; "))
}

print(screens.count == 1
    ? "Wallpaper updated on the connected display."
    : "Wallpaper updated on all \(screens.count) connected displays.")
