"""Golden for Test510 (3D Kepler matrix). dps=50; analytic Kepler propagation."""
from mpmath import mp, mpf, sqrt, sin, cos, atan2, floor, pi
import math
mp.dps=50

def cross(a,b): return [a[1]*b[2]-a[2]*b[1], a[2]*b[0]-a[0]*b[2], a[0]*b[1]-a[1]*b[0]]
def dot(a,b): return a[0]*b[0]+a[1]*b[1]+a[2]*b[2]
def norm(a): return sqrt(dot(a,a))
def rotate(v,axis,ang):
    c,s=cos(ang),sin(ang); cr=cross(axis,v)
    return [v[i]*c+cr[i]*s+axis[i]*dot(axis,v)*(1-c) for i in range(3)]

def kepler_state(mu,r0,v0,t):
    mu=mpf(mu); r0=[mpf(x) for x in r0]; v0=[mpf(x) for x in v0]; t=mpf(t)
    rn=norm(r0); v2=dot(v0,v0); rv=dot(r0,v0)
    hv=cross(r0,v0); hn=norm(hv)
    ev=[((v2-mu/rn)*r0[i]-rv*v0[i])/mu for i in range(3)]
    e=norm(ev); a=1/(2/rn-v2/mu); n=sqrt(mu/(a*a*a))
    if e < mpf('1e-12'):
        hh=[x/hn for x in hv]
        return rotate(r0,hh,n*t), rotate(v0,hh,n*t), a, e
    eh=[x/e for x in ev]; hh=[x/hn for x in hv]; qh=cross(hh,eh)
    cosE=(1-rn/a)/e; sinE=rv/(e*sqrt(mu*a))
    cc=sqrt(cosE*cosE+sinE*sinE); cosE/=cc; sinE/=cc
    E0=atan2(sinE,cosE); M0=E0-e*sin(E0); M=M0+n*t
    M=M-2*pi*floor((M+pi)/(2*pi)); E=M
    for _ in range(100): E-=(E-e*sin(E)-M)/(1-e*cos(E))
    den=1-e*cos(E); x=a*(cos(E)-e); y=a*sqrt(1-e*e)*sin(E)
    vx=-a*n*sin(E)/den; vy=a*n*sqrt(1-e*e)*cos(E)/den
    return [x*eh[i]+y*qh[i] for i in range(3)], [vx*eh[i]+vy*qh[i] for i in range(3)], a, e

mu=2.0; a=1.0
for e in [0.0,0.5,0.9,0.99]:
    periods=10 if e==0.99 else 100
    for inc in [0,45,90]:
        rp=float(a*(1-e)); vp=float(sqrt(mu*(1+e)/(a*(1-e))))
        ang=inc*math.pi/180.0
        r0=[rp,0.0,0.0]; v0=[0.0,float(vp*math.cos(ang)),float(vp*math.sin(ang))]
        rm=[mpf(x) for x in r0]; vm=[mpf(x) for x in v0]
        rr=norm(rm); vv=dot(vm,vm); aa=1/(2/rr-vv/mpf(mu)); nn=sqrt(mpf(mu)/(aa*aa*aa))
        T=float(periods*2*pi/nn)
        pt,vt,_,_=kepler_state(mu,r0,v0,T)
        print(f"// e={e} inc={inc} {periods}p")
        print("P0", [repr(x) for x in r0])
        print("V0", [repr(x) for x in v0])
        print("PT", [mp.nstr(x,40) for x in pt])
        print("VT", [mp.nstr(x,40) for x in vt])

