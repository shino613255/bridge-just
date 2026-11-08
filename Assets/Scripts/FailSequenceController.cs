using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// FAIL確定後の親子落下と橋の崩落を演出する。成功判定やスコアの更新はGameManagerが担当する。
/// </summary>
[DisallowMultipleComponent]
public sealed class FailSequenceController : MonoBehaviour
{
    [Header("Character fall (normal movement settings are untouched)")]
    [SerializeField, Min(0.1f)] private float gravity = 12f;
    [SerializeField, Min(0f)] private float horizontalSpeedMultiplier = 0.9f;
    [SerializeField, Min(0f)] private float airborneRotationMultiplier = 0.45f;
    [Header("Bridge collapse")]
    [SerializeField, Min(0.1f)] private float bridgeGravity = 10f;
    [SerializeField, Min(0f)] private float bridgeAngularSpeed = 65f;
    [SerializeField, Min(0f)] private float collapseDelay = 0.12f;
    [Header("Completion")]
    [SerializeField, Min(0f)] private float offscreenMargin = 0.2f;
    [SerializeField, Min(1f)] private float safetyTimeoutSeconds = 8f;
    [SerializeField] private bool drawFailurePath;

    public bool IsRunning { get; private set; }
    public bool ParentAirborne => parent != null && parent.Airborne;
    public bool ChildAirborne => child != null && child.Airborne;
    public bool HasChild => child != null;
    public bool BridgeCollapsing { get; private set; }
    public float LastDuration { get; private set; }
    public float SafetyTimeout => Safe(safetyTimeoutSeconds,8f,1f);
    public Vector3 ParentExit => parent != null ? parent.Route[parent.Route.Length-1] : Vector3.zero;
    private FallingActor parent, child;
    private BridgeBoardVisual visual;
    private bool cancelled;

    // 失敗が確定した瞬間の経路を保存し、その後の配置処理の影響を受けないようにする。
    public IEnumerator Play(CharacterMover circle, CircleCompanionStack companion,
        SpriteRenderer left, SpriteRenderer right, SpriteRenderer board, BridgeBoardVisual boardVisual,
        bool leftConnected, bool rightConnected, Camera camera, Action fallStarted)
    {
        Cancel(); cancelled=false; IsRunning=true; LastDuration=0f;
        visual=boardVisual;
        var surface=new BridgeBoardSurface(left,board);
        circle.CancelCrossing();
        float radius=BodyRadius(circle.GetComponent<SpriteRenderer>());
        float speed=circle.MoveSpeed;
        float fallGravity=Safe(gravity,12f,.1f);
        float horizontal=Safe(horizontalSpeedMultiplier,.9f,0f);
        parent=new FallingActor(circle.transform,radius,circle.RollingMultiplier,speed,
            BuildRoute(surface,left,circle.transform.position,radius,leftConnected));
        if(companion!=null && companion.TakeFailureControl(out Transform childBody,out float childRadius,
            out float childRolling,out float followDistance))
        {
            child=new FallingActor(childBody,childRadius,childRolling,speed,
                BuildRoute(surface,left,childBody.position,childRadius,leftConnected));
            // 小さい子供は端で親より低い位置を通るため、親が端から離れるまで落下開始を待つ。
            // 既存の追従距離を保ちつつ、落下中の親子が重なるのを防ぐ。
            float clearance=parent.Route[parent.Route.Length-1].y-child.Route[child.Route.Length-1].y
                +radius+childRadius+.06f;
            float vertical=parent.ReleaseVerticalSpeed(horizontal);
            float clearSeconds=(vertical+Mathf.Sqrt(vertical*vertical+2f*fallGravity*Mathf.Max(0f,clearance)))/fallGravity+.08f;
            float lag=Mathf.Max(followDistance/speed,clearSeconds);
            child.Delay=Mathf.Max(0f,parent.RouteLength/speed+lag-child.RouteLength/speed);
        }

        float started=Time.unscaledTime;
        bool soundPlayed=false;
        float collapseAt=-1f,collapseElapsed=0f;
        Vector3 leftEdge=BridgeSlopeGeometry.TopRight(left),rightEdge=BridgeSlopeGeometry.TopLeft(right);
        float gapCenterX=(leftEdge.x+rightEdge.x)*0.5f;
        Vector3 pivot=rightConnected&&!leftConnected ? BridgeSlopeGeometry.BottomRight(board)
            : leftConnected ? leftEdge : BridgeSlopeGeometry.BottomCenter(board);
        float turn=rightConnected&&!leftConnected ? 1f : -1f;
        float bottom=camera!=null ? camera.transform.position.y-camera.orthographicSize : -10f;
        float margin=Safe(offscreenMargin,.2f,0f);
        float rotation=Safe(airborneRotationMultiplier,.45f,0f);
        while(!cancelled && this && isActiveAndEnabled)
        {
            float dt=Time.deltaTime;
            LastDuration=Time.unscaledTime-started;
            if(LastDuration>=SafetyTimeout)
            {
                Debug.LogWarning("[BridgeJust] Failure presentation timed out; GAME OVER remains available.",this);
                break;
            }
            parent.Tick(dt,fallGravity,horizontal,rotation,gapCenterX,bottom-margin);
            child?.Tick(dt,fallGravity,horizontal,rotation,gapCenterX,bottom-margin,parent);
            if(parent.Airborne&&!soundPlayed) { soundPlayed=true; fallStarted?.Invoke(); }

            // 支持のない橋は親の落下後に崩し、親子のどちらかが乗っている橋は両方が離れるまで保つ。
            bool released=parent.Airborne && (!leftConnected || child==null || child.Airborne);
            if(released && collapseAt<0f)collapseAt=Time.time+Safe(collapseDelay,.12f,0f);
            if(collapseAt>=0f && Time.time>=collapseAt)
            {
                BridgeCollapsing=true;collapseElapsed+=dt;
                float angle=turn*Safe(bridgeAngularSpeed,65f,0f)*collapseElapsed;
                Vector3 drop=Vector3.down*(.5f*Safe(bridgeGravity,10f,.1f)*collapseElapsed*collapseElapsed);
                visual?.ApplyCollapse(pivot,drop,angle);
            }
            bool boardGone=visual==null || !visual.Visible || (BridgeCollapsing&&visual.TopY<bottom-margin);
            if(parent.Offscreen && (child==null||child.Offscreen) && boardGone)break;
            yield return null;
        }
        if(!soundPlayed&&!cancelled)fallStarted?.Invoke();
        IsRunning=false;
    }

    public void Cancel()
    {
        cancelled=true;IsRunning=false;BridgeCollapsing=false;
        parent=null;child=null;visual=null;
    }

    public static float BodyRadius(SpriteRenderer body)
        => body!=null ? body.localBounds.extents.x*Mathf.Abs(body.transform.lossyScale.x) : .4f;

    /// <summary>
    /// 失敗時に歩ける経路を作る。左足場と接続済みの橋までを使い、右足場には渡らせない。
    /// </summary>
    public static Vector3[] BuildRoute(BridgeBoardSurface surface,SpriteRenderer left,Vector3 start,float radius,bool useBridge)
    {
        var polygons=new List<Vector2[]>();
        if(useBridge)polygons.AddRange(surface.Polygons());
        polygons.Add(new[]{surface.Coordinates(BridgeSlopeGeometry.TopLeft(left)),surface.Coordinates(BridgeSlopeGeometry.TopRight(left))});
        Vector2[][] supports=polygons.ToArray();
        Vector2 first=surface.Coordinates(start);
        float end=useBridge?Mathf.Max(0f,surface.Max):0f;
        end=Mathf.Max(end,first.x);
        Vector3 At(float s)
        {
            float h=BridgeBoardSurface.SupportHeight(supports,s,radius);
            var p=surface.World(new Vector2(s,h));p.z=start.z;return p;
        }
        var route=new List<Vector3>{start};
        Vector3 supported=At(first.x);
        // 待機中に橋で持ち上がった分を、残った支持面まで滑らかに降ろす。
        if(Vector3.Distance(start,supported)>.0001f)route.Add(supported);
        void Sample(float a,Vector3 pa,float b,Vector3 pb,int depth)
        {
            float m=(a+b)*.5f;Vector3 pm=At(m);
            bool split=Mathf.Abs(b-a)>.02f || Vector3.Distance(pm,(pa+pb)*.5f)>.0001f
                || Vector3.Distance(At(Mathf.Lerp(a,b,.25f)),Vector3.Lerp(pa,pb,.25f))>.0001f
                || Vector3.Distance(At(Mathf.Lerp(a,b,.75f)),Vector3.Lerp(pa,pb,.75f))>.0001f;
            if(depth<14 && split) {Sample(a,pa,m,pm,depth+1);Sample(m,pm,b,pb,depth+1);}
            else route.Add(pb);
        }
        if(end>first.x+.00001f)Sample(first.x,supported,end,At(end),0);
        return route.ToArray();
    }

    private static float Safe(float value,float fallback,float minimum)
        => float.IsNaN(value)||float.IsInfinity(value)?fallback:Mathf.Max(minimum,value);

    private sealed class FallingActor
    {
        public readonly Transform Body;
        public readonly Vector3[] Route;
        public readonly float RouteLength;
        public float Radius => radius;
        public float Delay;
        public bool Airborne,Offscreen;
        private readonly float radius,rolling,speed;
        private int waypoint=1;
        private float airTime;
        private Vector3 release;
        public FallingActor(Transform body,float r,float multiplier,float moveSpeed,Vector3[] route)
        {
            Body=body;radius=Mathf.Max(.001f,r);rolling=multiplier;speed=moveSpeed;Route=route;
            for(int i=1;i<route.Length;i++)RouteLength+=Vector3.Distance(route[i-1],route[i]);
        }
        public float ReleaseVerticalSpeed(float horizontal)
        {
            // 足場から離れるときも斜面方向の速度を引き継ぐ。
            // 上向きの速度を急に0にすると、上り足場の角へ体が食い込んでしまう。
            for(int i=Route.Length-1;i>0;i--)
            {
                Vector3 tangent=Route[i]-Route[i-1];
                if(tangent.sqrMagnitude>.00000001f)return Mathf.Clamp(tangent.normalized.y,-.5f,.5f)*speed*horizontal;
            }
            return 0f;
        }
        public void Tick(float dt,float gravity,float horizontal,float spin,float gapX,float bottom,FallingActor leader=null)
        {
            if(Body==null)return;
            Vector3 previousPosition=Body.position,previousRelease=release;
            Quaternion previousRotation=Body.rotation;
            int previousWaypoint=waypoint;
            float previousAirTime=airTime;
            bool wasAirborne=Airborne,wasOffscreen=Offscreen;
            Advance(dt,gravity,horizontal,spin,gapX,bottom);
            // 小さい子供は曲面を短い距離で回れるため、親に追いつきそうなら経路上で待つ。
            // 横へ押し出したり、表面を横切って親を追い越したりしない。
            if(leader!=null && !wasAirborne && leader.Body!=null
                && Vector3.Distance(Body.position,leader.Body.position)<radius+leader.Radius+.15f)
            {
                Body.SetPositionAndRotation(previousPosition,previousRotation);
                release=previousRelease;waypoint=previousWaypoint;airTime=previousAirTime;
                Airborne=wasAirborne;Offscreen=wasOffscreen;
            }
        }

        private void Advance(float dt,float gravity,float horizontal,float spin,float gapX,float bottom)
        {
            if(Body==null||dt<=0f)return;
            if(Delay>0f){float used=Mathf.Min(dt,Delay);Delay-=used;dt-=used;}
            if(!Airborne)
            {
                float remaining=speed*dt;
                while(waypoint<Route.Length)
                {
                    Vector3 before=Body.position;
                    Body.position=Vector3.MoveTowards(before,Route[waypoint],remaining);
                    float distance=Vector3.Distance(before,Body.position);
                    Body.Rotate(0,0,-distance/radius*Mathf.Rad2Deg*rolling,Space.World);
                    remaining=Mathf.Max(0f,remaining-distance);
                    if(Body.position!=Route[waypoint])return;
                    Body.position=Route[waypoint];waypoint++;
                    if(remaining<=0f&&waypoint<Route.Length)return;
                }
                Airborne=true;release=Body.position;dt=speed>0?remaining/speed:0f;
            }
            airTime+=dt;
            // 落下中は足場の隙間へ徐々に寄せる。接続ぎりぎりの失敗でも右足場を貫通しないようにする。
            float distanceX=release.x-gapX;
            float rate=speed*horizontal/Mathf.Max(.1f,Mathf.Abs(distanceX));
            Body.position=new Vector3(gapX+distanceX*Mathf.Exp(-rate*airTime),
                release.y+ReleaseVerticalSpeed(horizontal)*airTime-.5f*gravity*airTime*airTime,release.z);
            Body.Rotate(0,0,-speed/radius*Mathf.Rad2Deg*rolling*spin*dt,Space.World);
            Offscreen=Body.position.y+radius<bottom;
        }
    }

    private void OnDrawGizmosSelected()
    {
        if(!drawFailurePath||!Application.isPlaying)return;
        void Draw(FallingActor actor,Color color){if(actor==null)return;Gizmos.color=color;for(int i=1;i<actor.Route.Length;i++)Gizmos.DrawLine(actor.Route[i-1],actor.Route[i]);Gizmos.DrawWireSphere(actor.Route[actor.Route.Length-1],actor.Radius);}
        Draw(parent,Color.cyan);Draw(child,Color.magenta);
    }
}
