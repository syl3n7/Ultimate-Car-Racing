# Manual Transmission System Guide

## Overview
The car controller now features a sophisticated manual transmission system that can be toggled between automatic and manual modes. The system includes realistic gear ratios, neutral gear handling, and proper reverse gear mechanics.

## Key Bindings

### Keyboard Controls
- **Shift Up**: `E` key
- **Shift Down**: `Q` key
- **Accelerate/Brake**: `W/S` or Arrow Keys
- **Steering**: `A/D` or Left/Right Arrow Keys

### Gamepad Controls
- **Shift Up**: Right Shoulder Button (RB/R1)
- **Shift Down**: Left Shoulder Button (LB/L1)
- **Accelerate/Brake**: Left Stick Up/Down
- **Steering**: Left Stick Left/Right

## Gear System

### Gear Layout
- **Reverse (-1)**: For backing up
- **Neutral (0)**: No power transmission, car coasts
- **1st through 7th Gear**: Forward gears with realistic ratios

### Gear Ratios
1. **1st Gear**: 3.82 (high torque, low speed)
2. **2nd Gear**: 2.26
3. **3rd Gear**: 1.64
4. **4th Gear**: 1.29
5. **5th Gear**: 1.06
6. **6th Gear**: 0.84
7. **7th Gear**: 0.62 (low torque, high speed)

### Special Features
- **Anti-Stall Protection**: Cannot shift to reverse while moving fast
- **Neutral Safety**: Must pass through neutral when going from forward to reverse
- **Speed-Limited Shifting**: Can only shift to reverse when speed < 5 km/h
- **Shift Delay**: 0.5 seconds between shifts to prevent rapid gear changes

## UI Display
- **Speed**: Displayed in KM/H
- **RPM**: Engine revolutions per minute
- **Gear**: Shows current gear (1-7, N for Neutral, R for Reverse)

## Settings (Inspector)
- **Use Manual Transmission**: Toggle between manual and automatic modes
- **Allow Reverse From Neutral**: Enable/disable shifting directly to reverse from neutral
- **Shift Delay**: Time between allowed gear changes (default: 0.5s)

## Engine Simulation
The system includes realistic engine behavior:
- **RPM Calculation**: Based on wheel speed and current gear ratio
- **Power Delivery**: Different torque characteristics per gear
- **Engine Sound**: RPM-based audio with gear shift sound effects
- **Idle RPM**: 800 RPM minimum
- **Red Line**: 8800 RPM maximum

## Usage Tips
1. **Starting**: Car starts in 1st gear, ready to go
2. **City Driving**: Use gears 1-3 for normal driving
3. **Highway**: Use gears 4-7 for high-speed driving
4. **Parking**: Shift to neutral to coast, use reverse for backing up
5. **Racing**: Manual control allows for optimal gear selection for track sections

## Troubleshooting
- **Car won't move**: Check if you're in neutral (N) - shift to 1st gear
- **Can't shift to reverse**: Make sure you're nearly stopped (< 5 km/h)
- **Rapid shifting not working**: Shift delay prevents this - wait 0.5 seconds between shifts
- **No gear shift sound**: Assign a gear shift audio clip in the inspector

This system provides an arcade-style driving experience while maintaining realistic transmission mechanics and engine simulation.
