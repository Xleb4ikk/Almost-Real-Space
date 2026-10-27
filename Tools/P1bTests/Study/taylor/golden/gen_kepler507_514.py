"""Golden for Test507/Test514. dps=60; analytic Kepler from exact double IC/T."""
from mpmath import mp, mpf, sqrt, sin, cos, atan2, floor, pi
mp.dps=60

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
    for _ in range(120): E-=(E-e*sin(E)-M)/(1-e*cos(E))
    den=1-e*cos(E); x=a*(cos(E)-e); y=a*sqrt(1-e*e)*sin(E)
    vx=-a*n*sin(E)/den; vy=a*n*sqrt(1-e*e)*cos(E)/den
    return [x*eh[i]+y*qh[i] for i in range(3)], [vx*eh[i]+vy*qh[i] for i in range(3)], a, e

cases=[
 ("507 ship e=0 10000p",1.0,[1.0,0.0,0.0],[0.0,1.0,0.0],62831.853071795864),
 ("507 ship e=0.9 1000p",1.0,[0.09999999999999998,0.0,0.0],[0.0,4.358898943540674,0.0],6283.1853071795795),
 ("507/514 ship e=0.99 1000p",1.0,[0.010000000000000009,0.0,0.0],[0.0,14.106735979665878,0.0],6283.185307179493),
]
for label,mu,r0,v0,T in cases:
    p,v,_,_=kepler_state(mu,r0,v0,T)
    print("##",label,"T",repr(T))
    print("P",[mp.nstr(x,40) for x in p])
    print("V",[mp.nstr(x,40) for x in v])

for label,mu,r0,v0,T in [
 ("507 nbody e=0.9 1000p",2.0,[0.09999999999999998,0.0,0.0],[0.0,6.164414002968977,0.0],4442.8829381583555),
 ("507 nbody e=0.99 1000p",2.0,[0.010000000000000009,0.0,0.0],[0.0,19.949937343259994,0.0],4442.882938158194),
]:
    p,v,_,_=kepler_state(mu,r0,v0,T)
    P=[-0.5*x for x in p]+[0.5*x for x in p]
    V=[-0.5*x for x in v]+[0.5*x for x in v]
    print("##",label,"T",repr(T))
    print("P",[mp.nstr(x,40) for x in P])
    print("V",[mp.nstr(x,40) for x in V])
