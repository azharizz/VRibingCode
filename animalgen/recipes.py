# Animal recipes: shape + colors. Blender space, meters: Z up, faces -Y, left = +X.
# Arm/leg points are for the LEFT side; the right side is mirrored.
# Every animal shares the same 19-bone skeleton and clips (one Unity Animator layout).
#
# Options
#   ears   : pointy | tufts | round | long | none
#   muzzle : snout | small | beak | none
#   tail   : bushy | thin | puff | none
#   arms   : arm | wing
#   face   : cheeks | mask | patches | plain      (head color pattern)
#   belly  : light belly patch on the body front
#   bulge  : frog-style eye bulges on top of the head

BASE = dict(
    fur="#E8782A", light="#FFF4E6", dark="#3A2A22", inner="#F2A894", eye="#161214",
    ear_color=None, limb=None, feet=None, accent="#F2B233", nose=None, eye_ring=None,
    head_center=(0.0, -0.02, 0.80), head_size=(0.25, 0.22, 0.21),
    eye_pos=(0.095, -0.215, 0.84), eye_scale=1.0, bulge=False,
    ears="pointy", ear_x=0.12, ear_len=0.22, ear_w=0.085,
    muzzle="snout", snout=0.10,
    face="cheeks", belly=True,
    body_r=[(0.17, 0.15), (0.165, 0.145), (0.11, 0.10)],
    arms="arm",
    arm=[(0.12, -0.02, 0.53), (0.21, -0.04, 0.43), (0.23, -0.06, 0.32)],
    leg=[(0.085, 0.0, 0.28), (0.095, -0.01, 0.17), (0.095, 0.0, 0.07), (0.095, -0.09, 0.045)],
    tail="bushy",
    tail_pts=[(0.0, 0.08, 0.27), (0.0, 0.28, 0.34), (0.0, 0.37, 0.50), (0.0, 0.36, 0.66), (0.0, 0.31, 0.76)],
    tail_r=[0.04, 0.085, 0.105, 0.085, 0.03],
)


def r(**kw):
    d = dict(BASE)
    d.update(kw)
    return d


RECIPES = {
    "fox": r(),
    "cat": r(
        fur="#9AA1AD", light="#F4F4F4", dark="#4B4E58", inner="#F3B3B8", nose="#E58A9A",
        ears="pointy", ear_len=0.17, ear_w=0.085, ear_x=0.13,
        muzzle="small", snout=0.06, face="plain",
        tail="thin",
        tail_pts=[(0.0, 0.08, 0.24), (0.0, 0.24, 0.24), (0.0, 0.35, 0.38), (0.0, 0.37, 0.58), (0.0, 0.30, 0.70)],
        tail_r=[0.035, 0.035, 0.035, 0.035, 0.03],
    ),
    "owl": r(
        fur="#8B5E3C", light="#F3E2C7", dark="#4A3020", eye_ring="#FFFFFF", feet="#F2B233",
        head_size=(0.26, 0.22, 0.22), eye_pos=(0.1, -0.205, 0.84), eye_scale=1.35,
        ears="tufts", ear_len=0.12, ear_w=0.06, ear_x=0.15,
        muzzle="beak", snout=0.07, face="mask",
        body_r=[(0.2, 0.18), (0.2, 0.18), (0.13, 0.12)], arms="wing",
        tail="puff", tail_r=[0.07],
    ),
    "bear": r(
        fur="#8A5A3B", light="#D9B38C", dark="#3B2618",
        head_size=(0.26, 0.22, 0.21),
        ears="round", ear_w=0.075, ear_x=0.16,
        muzzle="small", snout=0.08, face="plain",
        body_r=[(0.19, 0.17), (0.18, 0.16), (0.12, 0.11)],
        tail="puff", tail_r=[0.06],
    ),
    "rabbit": r(
        fur="#F2EEE8", light="#FFFFFF", dark="#CFC6BC", inner="#F5B5C0", nose="#E58A9A",
        ears="long", ear_len=0.30, ear_w=0.06, ear_x=0.07,
        muzzle="small", snout=0.05, face="plain", belly=False,
        tail="puff", tail_r=[0.07],
    ),
    "penguin": r(
        fur="#24262E", light="#F6F6F6", dark="#24262E", feet="#F2A33A", accent="#F2A33A",
        head_size=(0.24, 0.22, 0.21),
        ears="none", muzzle="beak", snout=0.07, face="mask",
        body_r=[(0.2, 0.18), (0.19, 0.17), (0.12, 0.11)], arms="wing",
        tail="none",
    ),
    "frog": r(
        fur="#5DBB4C", light="#D7F2B8", dark="#3E8A33", eye="#161214", feet="#3E8A33",
        head_size=(0.29, 0.22, 0.17), head_center=(0.0, -0.02, 0.76),
        eye_pos=(0.13, -0.15, 0.93), eye_scale=1.2, bulge=True,
        ears="none", muzzle="none", face="cheeks",
        tail="none",
    ),
    "panda": r(
        fur="#F5F5F2", light="#FFFFFF", dark="#202024", ear_color="#202024", limb="#202024", feet="#202024",
        ears="round", ear_w=0.075, ear_x=0.16,
        muzzle="small", snout=0.07, face="plain", belly=False,
        eye_ring="#202024", ring_scale=(1.05, 1.45), ring_tilt=-30,
        body_r=[(0.19, 0.17), (0.18, 0.16), (0.12, 0.11)],
        tail="puff", tail_r=[0.05],
    ),
}
