"""Golden for Test517 (Pythagorean three-body). dps=40, K=32, tol=1e-30, hmax=0.005."""
from mpmath import mp, mpf
from mpt_nbody import run_nbody
mp.dps=40
mu=[mpf(3),mpf(4),mpf(5)]
P0=[mpf(1),mpf(3),mpf(0), mpf(-2),mpf(-1),mpf(0), mpf(1),mpf(-1),mpf(0)]
V0=[mpf(0)]*9
for T in [2,5,10,20]:
    p,v,steps=run_nbody(mu,P0,V0,mpf(T),K=32,tol=mpf(10)**-30,hmax=mpf("0.005"))
    print("## T",T,"steps",steps)
    print("P",[mp.nstr(x,30) for x in p])
    print("V",[mp.nstr(x,30) for x in v])
