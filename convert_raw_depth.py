
import pathlib
import sys

import IPython
import cv2
import numpy as np
import matplotlib.pyplot as plt

# The unity output depth format for now is: GraphicsFormat.R32_SFloat
#   0.0 represents camera optical center
#   1.0 represents far clipping plane
# The default far clipping plane in unity is 1000.0.
# If this changes you must change the constant in this file
#
# The size of the image is implicit. Use the rgb image to get the size
#
# After converting the depth image to meters, we convert it to a fairly
# standard compressed format PNG 16-bit 1ch image where the units
# are in millimeters and 0 represents unknown or invalid depth

CLIPPING_PLANE_FAR = 1000.0

OUTPUT_CLIPPING_PLANE_FAR = 65.0  # limited by millimeter 16bit png output
OUTPUT_CLIPPING_PLANE_NEAR = 0.3
OUTPUT_UNITS = 1.0 # 1e-3

rgb_fn = pathlib.Path(sys.argv[1])
assert rgb_fn.exists(), rgb_fn
uid = rgb_fn.stem
raw_depth_fn = rgb_fn.parent / f'{uid}_depth.raw'
out_depth_fn = rgb_fn.parent / f'{uid}_depth.png'
assert raw_depth_fn.exists(), raw_depth_fn

rgb = cv2.imread(str(rgb_fn))
height, width, _ = rgb.shape
depth = np.fromfile(str(raw_depth_fn), dtype='<f')

# Convert depth to meters
depth = depth.reshape((height, width)) * CLIPPING_PLANE_FAR

# Invalidate depth values outside of expected ranges
valid = (
    (depth > OUTPUT_CLIPPING_PLANE_NEAR) &
    (depth < (OUTPUT_CLIPPING_PLANE_FAR - 1.0)) &
    np.isfinite(depth))
depth[~valid] = 0.0

# Convert depth to millimeters
depth = np.round(depth * 1000.0).astype(np.uint16)

# Save to png file
cv2.imwrite(str(out_depth_fn), depth)
