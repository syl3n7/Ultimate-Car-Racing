# GTA V Style Camera System

## Overview
The camera system has been completely rewritten to behave like GTA V's third-person driving camera - smooth, natural following behind the car with realistic banking effects.

## Key Features

### 🎮 **Smooth Following**
- **Dynamic Distance**: Camera automatically pulls back further when driving at high speed
- **Proper Height**: Camera maintains optimal height behind the car
- **Anticipation**: Camera looks slightly ahead based on car velocity for natural feel

### 🏎️ **Banking & Tilting**
- **Realistic Banking**: Camera tilts slightly when the car turns (like GTA V)
- **Smooth Transitions**: Banking angles transition smoothly based on car's angular velocity
- **Configurable**: Adjust banking sensitivity and maximum angle

### ⚙️ **Camera Settings (Inspector)**

#### **Follow Distance & Position**
- `Horizontal Distance` (8f): How far behind the car the camera sits
- `Vertical Height` (3f): Height of camera above the car/ground
- `Look At Height Offset` (1.5f): How much higher to look at on the car

#### **Car Following Behavior (NEW)**
- `Rotation Follow Strength` (0-1): How much camera follows car's rotation (1 = fully attached, 0 = world space)
- `Use Local Space` (true): Use car's coordinate system for positioning (more "attached" feel)
- `Show Debug Info` (false): Enable console debug output for troubleshooting

#### **Runtime Controls (Optional)**
- `Increase Distance Key` (Plus): Key to increase horizontal distance while playing
- `Decrease Distance Key` (Minus): Key to decrease horizontal distance while playing
- `Increase Height Key` (PageUp): Key to increase vertical height while playing
- `Decrease Height Key` (PageDown): Key to decrease vertical height while playing
- `Adjustment Step` (0.5f): How much to adjust distance/height per keypress

#### **Smoothing**
- `Position Smoothing` (3f): How smoothly camera follows car position
- `Rotation Smoothing` (2f): How smoothly camera rotates to look at car
- `Banking Smoothing` (1.5f): How smoothly banking transitions occur

#### **Banking & Tilting**
- `Max Bank Angle` (8f): Maximum tilt angle when turning
- `Banking Sensitivity` (1f): How responsive banking is to car turning

#### **Speed Effects**
- `Speed Pullback Multiplier` (0.02f): How much further back at high speed
- `Max Speed Pullback` (3f): Maximum additional distance at high speed

### 🖱️ **Mouse Control**
- **Temporary Override**: Move mouse to temporarily control camera manually
- **Auto Return**: Camera returns to auto-follow after 3 seconds of no mouse input
- **Smooth Transitions**: No jarring switches between manual and auto modes

## 🎛️ **NEW: Separate Horizontal & Vertical Controls**

### Why This Matters
Previously, adjusting the "follow distance" would affect both how far back AND how high the camera was positioned, which caused unwanted vertical movement when you only wanted to adjust horizontal distance.

### New Independent Controls
- **Horizontal Distance**: Controls ONLY how far back behind the car the camera sits
- **Vertical Height**: Controls ONLY how high above the car the camera is positioned  
- **Look At Height Offset**: Controls where on the car the camera looks (separate from camera height)

### Runtime Adjustment (While Playing)
You can now adjust camera positioning while driving:
- **+ / =**: Increase horizontal distance (camera moves further back)
- **- / _**: Decrease horizontal distance (camera moves closer)
- **Page Up**: Increase vertical height (camera moves higher)
- **Page Down**: Decrease vertical height (camera moves lower)

### Benefits
- ✅ **FIXED**: When stopped, adjusting distance only moves camera horizontally (no more weird vertical movement!)
- ✅ **Independent Control**: Vertical height stays constant when adjusting horizontal distance
- ✅ **Real-time Tuning**: Fine-tune camera position while driving without stopping
- ✅ **Range Sliders**: Inspector now has range sliders to prevent extreme values
- ✅ **Visual Feedback**: Console shows current values when adjusting (can be disabled)

## How It Works

### **Auto-Follow Mode** (Default)
1. **Position**: Camera positions itself behind the car at `followDistance`
2. **Height**: Maintains `followHeight` above ground level
3. **Banking**: Tilts based on car's turning (angular velocity)
4. **Speed Response**: Pulls back further when car is moving fast
5. **Look-Ahead**: Looks slightly ahead based on car's velocity direction

### **Mouse Control Mode** (Temporary)
1. **Manual Override**: When you move the mouse, camera switches to manual control
2. **Free Look**: Mouse controls camera rotation around the car
3. **Auto Return**: After 3 seconds of no mouse input, returns to auto-follow

## Benefits Over Previous System

✅ **Smoother**: No complex deadzone calculations or jarring transitions  
✅ **More Natural**: Banking effects make turns feel more dynamic  
✅ **Speed Responsive**: Camera behavior adapts to driving speed  
✅ **GTA V Feel**: Familiar and comfortable camera behavior  
✅ **Simpler Code**: Cleaner, more maintainable implementation  

## Fine-Tuning

### For Racing Games:
- Increase `Position Smoothing` to 4-5 for more responsive following
- Increase `Banking Sensitivity` to 1.5-2 for more dramatic banking

### For Casual Driving:
- Decrease `Banking Sensitivity` to 0.5-0.7 for subtler effects
- Increase `Speed Pullback Multiplier` for more dramatic speed effects

### For Cinematic Feel:
- Decrease `Rotation Smoothing` to 1-1.5 for slower, more cinematic camera movement
- Increase `Max Bank Angle` to 12-15 for more dramatic tilting

## Technical Notes

- Uses `linearVelocity` (updated Unity API) instead of deprecated `velocity`
- Banking calculated from car's `angularVelocity.y` component
- Smooth interpolation using `Vector3.SmoothDamp` and `Quaternion.Slerp`
- Velocity-based anticipation for natural look-ahead behavior

## 🔧 **NEW: Car Attachment System**

### The Problem
The previous camera system calculated positions in world space, which caused the camera to not follow the car's orientation changes smoothly. This made it feel detached from the car.

### The Solution
The new system uses the car's **local coordinate system** to position the camera, making it behave like it's "attached" to the car horizontally while still operating in world space to avoid Unity parenting issues.

### New Controls

#### **Use Local Space** (Default: ON)
- **ON**: Camera uses car's coordinate system - feels "attached" to the car
- **OFF**: Camera uses world space positioning - more detached feeling

#### **Rotation Follow Strength** (Default: 1.0)
- **1.0**: Camera fully follows car's rotation (most "attached" feeling)
- **0.5**: Blends between attached and world-space behavior
- **0.0**: Pure world-space positioning (least attached)

### How It Works
```
Car's Local Space:
- X axis: Left/Right
- Y axis: Up/Down  
- Z axis: Forward/Back

Camera Offset: (0, verticalHeight, -horizontalDistance)
- 0 on X = stay centered behind car
- verticalHeight on Y = height above car
- -horizontalDistance on Z = distance behind car
```

### Benefits of Local Space
- ✅ **Perfect Following**: Camera stays exactly behind the car regardless of car rotation
- ✅ **Smooth Turns**: Camera naturally follows when car turns or rotates
- ✅ **Consistent Distance**: Horizontal/vertical distances remain constant relative to car
- ✅ **No Drift**: Camera won't "drift away" from the car over time
