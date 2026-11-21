using System;
using System.Collections.Generic;
using AIChara;
using BepInEx;
using UnityEngine;

[BepInPlugin("local.hs2.eyelidfix","HS2 Eyelid Fix","1.0.0")]
public sealed class EyelidFix : BaseUnityPlugin
{
    float next;
    void Update()
    {
        if(Time.unscaledTime < next) return;
        next = Time.unscaledTime + 1;
        foreach(var face in FindObjectsOfType<CmpFace>()) {
            Attach(face.targetCustom.rendHead);
            Attach(face.targetCustom.rendEyelashes);
        }
    }
    static void Attach(Renderer renderer) {
            var r = renderer as SkinnedMeshRenderer;
            if(r && !r.GetComponent<EyelidUV>()) r.gameObject.AddComponent<EyelidUV>();
    }
}

[DefaultExecutionOrder(31000)]
public sealed class EyelidUV : MonoBehaviour
{
    SkinnedMeshRenderer head;
    Mesh source, instance;
    Vector2[] rest, frame;
    static readonly string[] names = {
        "e01_close", "e02_close_R", "e03_close_L", "e04_warai", "e04_warai_cl",
        "e05_damage", "e06_damage_R", "e07_damage_L", "e09_wink_R", "e10_wink_L",
        "e11_wink_cl", "e12_wink_half", "e14_ikari_cl"
    };
    readonly int[] shape = new int[names.Length];
    readonly Vector2[][] offset = new Vector2[names.Length][];
    readonly float[] weights = new float[names.Length];
    Vector3[] vertices;
    int[] triangles;
    bool reportedApplication;
    void Awake() { head=GetComponent<SkinnedMeshRenderer>(); }
    void Bind(Mesh mesh)
    {
        source=mesh;
        if(!mesh || !mesh.isReadable) return;
        bool lash = mesh.GetBlendShapeIndex("lash.e05_damage") >= 0;
        string prefix = lash ? "lash." : "head.";
        rest=mesh.uv;
        vertices=mesh.vertices;
        if(rest.Length!=vertices.Length) return;
        triangles=mesh.triangles;
        for(int s=0;s<names.Length;s++) {
            shape[s]=mesh.GetBlendShapeIndex(prefix+names[s]);
            offset[s]=null; weights[s]=0;
        }
        frame=new Vector2[rest.Length];
        instance=Instantiate(mesh);
        instance.name=mesh.name;
        Swap(instance);
        reportedApplication=false;
    }
    void Swap(Mesh next)
    {
        var weights=new float[next.blendShapeCount];
        for(int i=0;i<weights.Length;i++) weights[i]=head.GetBlendShapeWeight(i);
        head.sharedMesh=next;
        for(int i=0;i<weights.Length;i++) head.SetBlendShapeWeight(i,weights[i]);
    }
    static float Cross(Vector2 a, Vector2 b) { return a.x*b.y-a.y*b.x; }
    public static Vector2[] Solve(Mesh mesh,int shapeIndex,Vector3[] pos,Vector2[] uv,int[] tri)
    {
        int n=uv.Length;
        var shifts=new Vector2[n];
        if(shapeIndex<0) return shifts;
        var delta=new Vector3[n];
        int key=mesh.GetBlendShapeFrameCount(shapeIndex)-1;
        if(key<0) return shifts;
        mesh.GetBlendShapeFrameVertices(shapeIndex,key,delta,null,null);
        var edgeCounts=new Dictionary<long,int>();
        var border=new bool[n];
        for(int k=0;k<tri.Length;k++) {
            int a=tri[k], b=tri[k-k%3+(k%3+1)%3];
            long id=((long)Math.Min(a,b)<<32)|(uint)Math.Max(a,b);
            int count; edgeCounts.TryGetValue(id,out count); edgeCounts[id]=count+1;
        }
        foreach(var pair in edgeCounts) if(pair.Value!=2) {
            border[(int)(pair.Key>>32)]=true; border[(int)(pair.Key & 0xffffffffL)]=true;
        }
        // Give stretched triangles room to borrow texture area from the
        // adjacent lid surface while keeping the rest of the face fixed.
        var region = new bool[n];
        bool ordinaryClose = mesh.GetBlendShapeName(shapeIndex).Contains("_close");
        for (int v = 0; v < n; v++)
            region[v] = ordinaryClose ? delta[v].y < -.0001f : delta[v].sqrMagnitude > 1e-8f;
        for (int ring = 0; ring < 3; ring++) {
            var expanded = (bool[])region.Clone();
            for (int k = 0; k < tri.Length; k++) {
                if (!region[tri[k]]) continue;
                int start = k - k % 3;
                for (int j = 0; j < 3; j++) expanded[tri[start+j]] = true;
            }
            region = expanded;
        }
        var links=new List<int>[n];
        for(int i=0;i<tri.Length;i++) {
            int v=tri[i];
            if(border[v] || !region[v]) continue;
            if(links[v]==null) links[v]=new List<int>();
            links[v].Add(i);
        }
        // Match UV edge lengths to deformed surface lengths. Pin open borders
        // and reject steps that would flip or collapse a texture triangle.
        for(int pass=0;pass<40;pass++) for(int v=0;v<n;v++) {
            if(links[v]==null) continue;
            var goal=Vector2.zero; float sum=0;
            foreach(int k in links[v]) {
                int t=k-k%3;
                for(int j=1;j<=2;j++) {
                    int b=tri[t+(k%3+j)%3];
                    var e=pos[v]-pos[b];
                    float len=e.magnitude;
                    if(len<1e-6f) continue;
                    float scale=Mathf.Clamp((e+delta[v]-delta[b]).magnitude/len,.4f,6f);
                    float w=1f/len;
                    goal+=w*(shifts[b]+(uv[v]-uv[b])*(scale-1));
                    sum+=w;
                }
            }
            if(sum==0) continue;
            var candidate=Vector2.Lerp(shifts[v],Vector2.ClampMagnitude(goal/sum,.018f),.65f);
            foreach(int k in links[v]) {
                int t=k-k%3,b=tri[t+(k%3+1)%3],c=tri[t+(k%3+2)%3];
                float old=Cross(uv[b]-uv[v],uv[c]-uv[v]);
                var point=uv[v]+candidate;
                if(Mathf.Abs(old)>1e-10f && Cross(uv[b]+shifts[b]-point,uv[c]+shifts[c]-point)/old<.35f) {
                    var from = uv[v] + shifts[v];
                    float currentArea = Cross(uv[b]+shifts[b]-from, uv[c]+shifts[c]-from)/old;
                    float proposedArea = Cross(uv[b]+shifts[b]-point, uv[c]+shifts[c]-point)/old;
                    float fraction = Mathf.Clamp01((currentArea-.36f)/(currentArea-proposedArea));
                    candidate = Vector2.Lerp(shifts[v], candidate, fraction);
                }
            }
            shifts[v]=candidate;
        }
        return shifts;
    }
    void LateUpdate()
    {
        var mesh=head.sharedMesh;
        if(instance ? mesh!=instance : mesh!=source) {
            if(instance) Destroy(instance);
            instance=null;
            Bind(mesh);
        }
        if(!instance || head.sharedMesh!=instance) return;
        bool changed=false;
        for(int s=0; s<shape.Length; s++) {
            float value=ReadWeight(s);
            changed |= value!=weights[s];
            weights[s]=value;
        }
        if(!changed) return;
        // The paired close shapes share one budget per side. Independent
        // expressions only consume budget where their solved offsets overlap.
        for(int s=0;s<shape.Length;s++)
            if(weights[s]>0 && offset[s]==null)
                offset[s]=Solve(source,shape[s],vertices,rest,triangles);
        Array.Copy(rest,frame,rest.Length);
        for(int v=0;v<rest.Length;v++) {
            Vector2 shift=Vector2.zero;
            float sum=0;
            for(int s=0;s<shape.Length;s++) {
                float w=weights[s];
                if(w==0 || offset[s][v].sqrMagnitude<1e-18f) continue;
                if(s==1 || s==2) w=Mathf.Min(w,1-weights[0]);
                shift+=offset[s][v]*w;
                sum+=w;
            }
            frame[v]+=shift/Mathf.Max(1f,sum);
        }
        instance.uv=frame;
        if(!reportedApplication && Array.Exists(weights, w => w>.2f)) {
            int moved=0;
            for(int i=0;i<rest.Length;i++) if((frame[i]-rest[i]).sqrMagnitude>1e-12f) moved++;
            Debug.Log("[HS2 Eyelid Fix] "+head.name+" moved UV vertices "+moved+"/"+rest.Length);
            reportedApplication=true;
        }
    }
    float ReadWeight(int slot) { return shape[slot]<0 ? 0f : Mathf.Clamp01(head.GetBlendShapeWeight(shape[slot])*.01f); }
    void OnDestroy()
    {
        if(instance && head && head.sharedMesh==instance && source) Swap(source);
        if(instance) Destroy(instance);
    }
}







