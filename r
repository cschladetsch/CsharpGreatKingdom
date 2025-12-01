#!/usr/bin/env python3
import sys
import time
import subprocess
import os

# Configuration
PROJECT = "GreatKingdom"
LOG_FILE = "build.log"
BUILD_CMD = ["dotnet", "build", "-c", "Release", "--nologo", "-v", "q"]
RUN_CMD = ["dotnet", "run", "-c", "Release", "--no-build", "--project", PROJECT]

def hide_cursor():
    sys.stdout.write("\033[?25l")
    sys.stdout.flush()

def show_cursor():
    sys.stdout.write("\033[?25h")
    sys.stdout.flush()

def main():
    hide_cursor()
    print("  Compiling Castles... [   ]", end="", flush=True)

    # 1. Start the Build Process
    with open(LOG_FILE, "w") as log:
        process = subprocess.Popen(BUILD_CMD, stdout=log, stderr=log)

    # 2. Spinner Animation Loop
    spinner = "|/-\\"
    idx = 0
    
    try:
        while process.poll() is None:
            # Move cursor back 3 positions (inside the brackets)
            sys.stdout.write("\b\b\b")
            sys.stdout.write(f"{spinner[idx]} ]")
            sys.stdout.flush()
            
            idx = (idx + 1) % len(spinner)
            time.sleep(0.1)

        # 3. Handle Result
        if process.returncode == 0:
            # Success: Retroactive Progress Bar Animation
            # Move back into the brackets
            sys.stdout.write("\b\b\b  ") # Clear the spinner char
            sys.stdout.write("\b\b\b")   # Move back
            
            # Fill animation
            for _ in range(5):
                sys.stdout.write("▓▓")
                sys.stdout.flush()
                time.sleep(0.05)
            
            print(" ] Ready!")
            show_cursor()
            
            # 4. Run the Game
            subprocess.run(RUN_CMD)
            
        else:
            # Failure
            show_cursor()
            print("\n\n❌ Build Failed! Error Log:")
            print("-" * 30)
            with open(LOG_FILE, "r") as log:
                print(log.read())
            print("-" * 30)
            sys.exit(process.returncode)

    except KeyboardInterrupt:
        # Handle Ctrl+C gracefully
        show_cursor()
        print("\nCancelled.")
        sys.exit(1)
    finally:
        show_cursor()

if __name__ == "__main__":
    main()
