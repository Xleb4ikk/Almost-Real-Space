"""Golden for Test511. dps=50; polynomial Taylor expansions at 0."""
from mpmath import mp, mpf, taylor
mp.dps=50
a=[4.0,0.3,-0.2,0.1,0.05,-0.02,0.01]
b=[2.0,-0.1,0.04,0.02,-0.01,0.005,0.002]
K=6
def A(t): return sum(mpf(a[k])*t**k for k in range(K+1))
def B(t): return sum(mpf(b[k])*t**k for k in range(K+1))
def show(name,vals):
    print(name, [mp.nstr(x,40) for x in vals])
show("sqrt", taylor(lambda t: mp.sqrt(A(t)),0,K))
show("div", taylor(lambda t: A(t)/B(t),0,K))
show("mul", taylor(lambda t: A(t)*B(t),0,K))
show("comp", taylor(lambda t: (A(t)*B(t))/(mp.sqrt(A(t))+B(t)),0,K))
show("der", [mpf(a[k+1])*(k+1) for k in range(K)]+[mpf(0)])
show("int", [mpf(0)]+[mpf(a[k-1])/k for k in range(1,K+1)])
