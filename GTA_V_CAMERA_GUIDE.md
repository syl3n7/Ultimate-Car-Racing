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
- `Follow Distance` (8f): How far behind the car the camera sits
- `Follow Height` (3f): Height of camera above ground
- `Height Offset` (1.5f): How much higher to look at on the car

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
