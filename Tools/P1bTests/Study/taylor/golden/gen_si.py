"""Golden for Test509 (Sun-Earth-Moon, SI). dps=50, K=40, tol=1e-40, hmax=86400."""
from mpmath import mp, mpf, sqrt, cos, sin, pi
from mpt_nbody import run_nbody
mp.dps=50
mu=[mpf(float(x)) for x in ["1.32712440018e20","3.986004418e14","4.9048695e12"]]
r_e=float("1.495978707e11"); r_m=float("3.844e8")
v_e=float(mp.sqrt(mu[0]/mpf(r_e))); v_m=float(mp.sqrt(mu[1]/mpf(r_m)))
T=31557600.0
for label,inc in [("planar",0.0),("Moon 30 deg",30.0)]:
    ci=float(cos(inc*pi/180)); si=float(sin(inc*pi/180))
    P0f=[0.0,0.0,0.0, r_e,0.0,0.0, r_e+r_m,0.0,0.0]
    vsy=-(mu[1]*mpf(v_e)+mu[2]*mpf(v_e+v_m*ci))/mu[0]
    vsz=-(mu[2]*mpf(v_m*si))/mu[0]
    V0f=[0.0,float(vsy),float(vsz), 0.0,v_e,0.0, 0.0,v_e+v_m*ci,v_m*si]
    P0=[mpf(x) for x in P0f]; V0=[mpf(x) for x in V0f]
    p,v,steps=run_nbody(mu,P0,V0,mpf(T),K=40,tol=mpf(10)**-40,hmax=mpf(86400))
    print("##",label,"steps",steps)
    print("P0",[repr(x) for x in P0f])
    print("V0",[repr(x) for x in V0f])
    print("PT",[mp.nstr(x,40) for x in p])
    print("VT",[mp.nstr(x,40) for x in v])
