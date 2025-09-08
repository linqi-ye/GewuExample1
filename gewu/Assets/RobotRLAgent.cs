using UnityEngine;
using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Sensors;
using Random = UnityEngine.Random;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using Unity.Sentis;
using XCharts.Runtime;


public class RobotRLAgent : Agent
{
    
    int tp = 0;
    int tq = 0;
    int tt = 0;
    public bool fixbody = false;
    public bool train;
    public bool accelerate;
    float uff = 0;
    float uf1 = 0;
    float uf2 = 0;
    float[] u = new float[16] { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 };
    float[] ut = new float[16] { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 };
    float[] utt = new float[16] { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 };
    float[] utotal = new float[16] { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0};
    int T1 = 50;
    int T2 = 30;
    int tp0 = 0;
    public enum StyleB
    {
        walk = 0,
        run = 1,
        jump = 2
    }
    public enum StyleQ
    {
        trot = 0,
        bound = 1,
        pronk = 2
    }
    public enum StyleL
    {
        drive = 0,
        walk = 1,
        jump = 2
    }
    public enum StyleR
    {
        biped = 0,
        quadruped = 1,
        legwheeled = 2
    }
    
    Transform body;
    public int ObservationNum;
    public int ActionNum;
    [Header("RobotType")]
    public StyleR Robot;
    [Header("Biped")]
    public StyleB BipedTargetMotion;
    public ModelAsset BWalkPolicy;
    public ModelAsset BRunPolicy;
    public ModelAsset BJumpPolicy; 
    [Header("Quadruped")]
    public StyleQ QuadrupedTargetMotion;
    public ModelAsset QTrotPolicy;
    public ModelAsset QBoundPolicy;
    public ModelAsset QPronkPolicy;
    [Header("Legwheeled")]
    public StyleL LegwheelTargetMotion;
    public ModelAsset LDrivePolicy;
    public ModelAsset LWalkPolicy;
    public ModelAsset LJumpPolicy;

    StyleB LastBmotion;
    StyleQ LastQmotion;
    StyleL LastLmotion;

    List<float> P0 = new List<float>();
    List<float> W0 = new List<float>();
    List<Transform> bodypart = new List<Transform>();
    Vector3 pos0;
    Quaternion rot0;
    ArticulationBody[] arts = new ArticulationBody[40];
    ArticulationBody[] acts = new ArticulationBody[16];
    GameObject robot;

    float[] kb = new float[16] { 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30 };
    float[] kb1 = new float[16] { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 };
    float[] kb2 = new float[16] { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 };
    float dh = 25;
    float d0 = 15;
    float ko = 2;
    float kh = 0;

    public override void Initialize()
    {
        
        arts = this.GetComponentsInChildren<ArticulationBody>();
        ActionNum = 0;
        for (int k = 0; k < arts.Length; k++)
        {
            if(arts[k].jointType.ToString() == "RevoluteJoint")
            {
                acts[ActionNum] = arts[k];
                print(acts[ActionNum]);
                ActionNum++;
            }
        }
        body = arts[0].GetComponent<Transform>();
        pos0 = body.position;
        rot0 = body.rotation;
        arts[0].GetJointPositions(P0);
        arts[0].GetJointVelocities(W0);
        accelerate = train;
    }

    private bool _isClone = false; 
    void Start()
    {
        Time.fixedDeltaTime = 0.01f;

        /*SerializedObject tagManager = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset"));
        SerializedProperty layers = tagManager.FindProperty("layers");
        SerializedProperty layer = layers.GetArrayElementAtIndex(15);
        int targetLayer = LayerMask.NameToLayer("robot");
        layer.stringValue = "robot";
        tagManager.ApplyModifiedProperties();
        Physics.IgnoreLayerCollision(15, 15, true);
        ChangeLayerRecursively(gameObject, 15);*/

        int numrob=8;
        if(train)numrob=32;
        if (!_isClone) 
        {
            for (int i = 1; i < numrob; i++)
            {
                GameObject clone = Instantiate(gameObject); 
                clone.transform.position = transform.position + new Vector3(i * 2f, 0, 0);
                clone.name = $"{name}_Clone_{i}"; 
                clone.GetComponent<RobotRLAgent>()._isClone = true; 
            }
        }
    }
    void ChangeLayerRecursively(GameObject obj, int targetLayer)
    {
        obj.layer = targetLayer;
        foreach (Transform child in obj.transform)ChangeLayerRecursively(child.gameObject, targetLayer);
    }

    public override void OnEpisodeBegin()
    {
        tp = 0;
        tq = 0;
        tt = 0;
        for (int i = 0; i< 16; i++) u[i] = 0;
        for (int i = 0; i < 16; i++) ut[i] = 0;
        for (int i = 0; i < 16; i++) utt[i] = 0;

        
        Quaternion randRot = rot0 * Quaternion.Euler(0, Random.Range(-180f,180f), 0);
        float px;
        float pz;
        if(Random.Range(0,2)==0)
        {
            px = 4*(Random.Range(0,2)*2-1);
            pz = Random.Range(-4f,4f);
        }
        else
        {
            pz = 4*(Random.Range(0,2)*2-1);
            px = Random.Range(-4f,4f);
        }
        
        Vector3 randPos = new Vector3(pos0[0]+px, pos0[1], pos0[2]+pz);
        
        ObservationNum = 9 + 2 * ActionNum;
        if (fixbody) arts[0].immovable = true;
        if (!fixbody)
        {
            arts[0].TeleportRoot(randPos, randRot);
            //arts[0].TeleportRoot(pos0, rot0);
            arts[0].velocity = Vector3.zero;
            arts[0].angularVelocity = Vector3.zero;
            arts[0].SetJointPositions(P0);
            arts[0].SetJointVelocities(W0);
        }
    }

    public override void CollectObservations(VectorSensor sensor)
    {
        sensor.AddObservation(body.InverseTransformDirection(Vector3.down));
        sensor.AddObservation(body.InverseTransformDirection(arts[0].angularVelocity));
        sensor.AddObservation(body.InverseTransformDirection(arts[0].velocity));
        for (int i = 0; i < ActionNum; i++)
        {
            sensor.AddObservation(acts[i].jointPosition[0]);
            sensor.AddObservation(acts[i].jointVelocity[0]);
        }
    }
    float EulerTrans(float eulerAngle)
    {
        if (eulerAngle <= 180)
            return eulerAngle;
        else
            return eulerAngle - 360f;
    }
    public override void OnActionReceived(ActionBuffers actionBuffers)
    {
        for (int i = 0; i < 16; i++) utotal[i] = 0;
        var continuousActions = actionBuffers.ContinuousActions;
        var kk = 0.9f;
        
        for (int i = 0; i < ActionNum; i++)
        {
            u[i] = u[i] * kk + (1 - kk) * continuousActions[i];
            ut[i] += u[i];
            utt[i] += ut[i];
            utotal[i] = kb[i] * u[i] + kb1[i] * ut[i] + kb2[i] * utt[i];
            if (fixbody) utotal[i] = 0;
        }

        string name = this.name;
        int[] idx = new int[6] { 2, 3, 4, 7, 8, 9 };
        float[] ktemp1 = new float[12] { 5, 5, 30, 60, 30, 5, 5, 30, 60, 30, 0, 0 };
        d0 = 0;
        if (name.Contains("Tinker"))
        {
            idx = new int[6] { 2, 3, 4, 7, 8, 9 };
            ktemp1 = new float[12] { 5, 5, 30, 60, 30, 5, 5, 30, 60, 30, 0, 0 };
            ko = 01f;
            d0 = 20;
        }
        if (name.Contains("TaiTan"))
        {
            idx = new int[6] { 2, 3, 4, -7, -8, 9 };
            ktemp1 = new float[12] { 5, 5, 30, 60, 30, 5, 5, 30, 60, 30, 0, 0 };
            ko = 1f;
        }
        if (name.Contains("Birdy"))
        {
            idx = new int[6] { 2, 3, 4, 7, 8, 9 };
            ktemp1 = new float[12] { 5, 5, 30, 60, 30, 5, 5, 30, 60, 30, 0, 0 };
            ko = 01f;
            d0 = 40;
        }
        if (Robot == StyleR.biped && ActionNum == 10)
        {
            if (BipedTargetMotion == StyleB.walk)
            {
                for (int i = 0; i < 12; i++) kb[i] = ktemp1[i];
                T1 = 30;
                dh = 20;
                utotal[Mathf.Abs(idx[0])] += (dh * uf1 + d0) * Mathf.Sign(idx[0]);
                utotal[Mathf.Abs(idx[1])] -= 2 * (dh * uf1 + d0) * Mathf.Sign(idx[1]);
                utotal[Mathf.Abs(idx[2])] += (dh * uf1 + d0) * Mathf.Sign(idx[2]);
                utotal[Mathf.Abs(idx[3])] += (dh * uf2 + d0) * Mathf.Sign(idx[3]);
                utotal[Mathf.Abs(idx[4])] -= 2 * (dh * uf2 + d0) * Mathf.Sign(idx[4]);
                utotal[Mathf.Abs(idx[5])] += (dh * uf2 + d0) * Mathf.Sign(idx[5]);
                if (!train) SetModel("gewu", BWalkPolicy);
            }
            if (BipedTargetMotion == StyleB.run)
            {
                float[] ktemp = new float[12] { 5, 9, 40, 60, 40, 5, 9, 40, 60, 40, 0, 0 };
                for (int i = 0; i < 12; i++) kb[i] = ktemp[i];
                T1 = 20;
                dh = 20;
                d0 = 15;
                utotal[2] += (dh * uf1 + d0);
                utotal[3] -= 2 * (dh * uf1 + d0);
                utotal[4] += (dh * uf1 + d0);
                utotal[7] += (dh * uf2 + d0);
                utotal[8] -= 2 * (dh * uf2 + d0);
                utotal[9] += (dh * uf2 + d0);
                if (!train) SetModel("gewu", BRunPolicy);
                if (tt > 900 && kh == 0)
                {
                    kh = 2;
                    ko = 4;
                    print(222222222222222);
                }
            }
            if (BipedTargetMotion == StyleB.jump)
            {
                float[] ktemp = new float[12] { 5, 5, 40, 60, 40, 5, 5, 40, 60, 40, 0, 0 };
                for (int i = 0; i < 12; i++) kb[i] = ktemp[i];
                T1 = 30;
                dh = 40;
                d0 = 5;
                utotal[2] += dh * uff + d0;
                utotal[3] -= (dh * uff + d0) * 2;
                utotal[4] += dh * uff + d0;
                utotal[7] = utotal[2];
                utotal[8] = utotal[3];
                utotal[9] = utotal[4];
                //utotal[7] += dh * uff + d0;
                //utotal[8] -= (dh * uff + d0) * 2;
                //utotal[9] += dh * uff + d0;
                if (!train) SetModel("gewu", BJumpPolicy);
                if (tt > 900 && kh == 0)
                {
                    kh = 2;
                    print(222222222222222);
                }
            }
        }

        idx = new int[6] { 3, 4, 5, 9, 10, 11 };
        ktemp1 = new float[12]{ 10, 10, 45, 20, 40, 10,   10, 10, 40, 20, 45, 10 };
        dh = 10;
        d0 = 0;
        if (name.Contains("H1"))
        {
            idx = new int[6] { -2, -4, -5, -8, -10, -11 };
            ktemp1 = new float[12] { 10, 30, 10, 30, 30, 10,    10, 30, 10, 30, 30, 10 };
            ko = 01f;
            d0 = 10;
            dh = 10;
        }
        if (name.Contains("G1"))
        {
            idx = new int[6] { -1, -4, -5, -7, -10, -11 };
            ktemp1 = new float[12] { 60, 40, 20, 30, 60, 40,    60, 40, 20, 30, 60, 40 };
            ko = 01f;
            d0 = 20;
            dh = 40;
        }
        if (name.Contains("Mlg"))
        {
            idx = new int[6] { 3, 4, 5, -9, -10, -11 };
            ktemp1 = new float[12] { 40, 10, 10, 20, 40, 10, 40, 10, 10, 20, 40, 10 };
            dh = 30;
        }
        if (name.Contains("MiniLoong"))
        {
            idx = new int[6] { -1, 4, -5, 7, -10, -11 };
            ktemp1 = new float[12] { 40, 30, 30, 30, 40, 30, 40, 30, 30, 30, 40, 30 };
            ko = 01f;
            dh = 30;
        }
        if (name.Contains("V2.5"))
        {
            idx = new int[6] { -3, -4, -5, -9, -10, -11 };
            ktemp1 = new float[12] { 40, 10, 10, 20, 40, 10, 40, 10, 10, 20, 40, 10 };
            ko = 0.1f;
            dh = 30;
        }
        if (name.Contains("T1"))
        {
            idx = new int[6] { -1, -4, -5, -7, -10, -11 };
            ktemp1 = new float[12] { 40, 10, 10, 20, 40, 10, 40, 10, 10, 20, 40, 10 };
            ko = 01f;
            d0 = 5;
            dh = 30;
        }
        if (name.Contains("zqsa01"))
        {
            idx = new int[6] { -3, -4, -5, -9, -10, -11 };
            ktemp1 = new float[12] { 40, 10, 10, 20, 40, 10, 40, 10, 10, 20, 40, 10 };
            ko = 01f;
            d0 = 0;
            dh = 40;
        }
        if (Robot == StyleR.biped && ActionNum == 12)
        {
            if (BipedTargetMotion == StyleB.walk)
            {
                for (int i = 0; i < 12; i++) kb[i] = ktemp1[i];
                T1 = 30;
                utotal[Mathf.Abs(idx[0]) - 1] += (dh * uf1 + d0) * Mathf.Sign(idx[0]);
                utotal[Mathf.Abs(idx[1]) - 1] -= 2 * (dh * uf1 + d0) * Mathf.Sign(idx[1]);
                utotal[Mathf.Abs(idx[2]) - 1] += (dh * uf1 + d0) * Mathf.Sign(idx[2]);
                utotal[Mathf.Abs(idx[3]) - 1] += (dh * uf2 + d0) * Mathf.Sign(idx[3]);
                utotal[Mathf.Abs(idx[4]) - 1] -= 2 * (dh * uf2 + d0) * Mathf.Sign(idx[4]);
                utotal[Mathf.Abs(idx[5]) - 1] += (dh * uf2 + d0) * Mathf.Sign(idx[5]);
                if (!train) SetModel("gewu", BWalkPolicy);
            }
            if (BipedTargetMotion == StyleB.run)
            {
                float[] ktemp = new float[12] { 10, 10, 60, 30, 60, 10,    10, 10, 60, 30, 60, 10 };
                for (int i = 0; i < 12; i++) kb[i] = ktemp[i];
                T1 = 25;
                dh = 40;
                d0 = 20;
                utotal[2] += (dh * uf1 + d0);
                utotal[3] -= 2 * (dh * uf1 + d0);
                utotal[4] += (dh * uf1 + d0);
                utotal[8] += (dh * uf2 + d0);
                utotal[9] -= 2 * (dh * uf2 + d0);
                utotal[10] += (dh * uf2 + d0);
                if (!train) SetModel("gewu", BRunPolicy);
                if (tt > 900 && kh == 0)
                {
                    kh = 2;
                    //ko = 4;
                    print(222222222222222);
                }
            }
            if (BipedTargetMotion == StyleB.jump)
            {
                float[] ktemp = new float[12] { 10, 10, 40, 50, 40, 10,   10, 10, 40, 50, 40, 10 };
                for (int i = 0; i < 12; i++) kb[i] = ktemp[i];
                T1 = 30;
                dh = 50;
                d0 = 5;
                utotal[2] += dh * uff + d0;
                utotal[3] -= (dh * uff + d0) * 2;
                utotal[4] += dh * uff + d0;
                //utotal[8] = utotal[0];
                //utotal[9] = utotal[3];
                //utotal[10] = utotal[4];
                utotal[8] += dh * uff + d0;
                utotal[9] -= (dh * uff + d0) * 2;
                utotal[10] += dh * uff + d0;
                if (!train) SetModel("gewu", BJumpPolicy);
                if (tt > 900 && kh == 0)
                {
                    kh = 4;
                    print(222222222222222);
                }
            }
        }
        if (Robot == StyleR.quadruped)
        {
            d0 = 30;
            dh = 40;
            ko = 1;
            T1 = 30;
            if (QuadrupedTargetMotion == StyleQ.trot)
            {
                float[] ktemp = new float[12] { 40, 60, 40, 40, 60, 40, 40, 60, 40, 40, 60, 40 };
                for (int i = 0; i < 12; i++) kb[i] = ktemp[i];
                
                utotal[1] += dh * uf1 + d0;
                utotal[2] += (dh * uf1 + d0) * -2;
                utotal[4] += dh * uf2 + d0;
                utotal[5] += (dh * uf2 + d0) * -2;
                utotal[7] += dh * uf2 + d0;
                utotal[8] += (dh * uf2 + d0) * -2;
                utotal[10] += dh * uf1 + d0;
                utotal[11] += (dh * uf1 + d0) * -2;
                if (!train) SetModel("gewu", QTrotPolicy);
            }
            if (QuadrupedTargetMotion == StyleQ.bound)
            {
                utotal[1] += dh * uf1 + d0;
                utotal[2] += (dh * uf1 + d0) * -2;
                utotal[4] += dh * uf1 + d0;
                utotal[5] += (dh * uf1 + d0) * -2;
                utotal[7] += dh * uf2 + d0;
                utotal[8] += (dh * uf2 + d0) * -2;
                utotal[10] += dh * uf2 + d0;
                utotal[11] += (dh * uf2 + d0) * -2;
                if (!train) SetModel("gewu", QBoundPolicy);
            }
            if (QuadrupedTargetMotion == StyleQ.pronk)
            {
                float[] ktemp = new float[12] { 5, 30, 60, 5, 30, 60, 5, 30, 60, 5, 30, 60 };
                for (int i = 0; i < 12; i++) kb[i] = ktemp[i];
                T2 = 30;
                dh = 40;
                d0 = 30;
                ko = 2;
                kh = 1;
                utotal[1] += dh * uff + d0;
                utotal[2] += (dh * uff + d0) * -2;
                utotal[4] += dh * uff + d0;
                utotal[5] += (dh * uff + d0) * -2;
                utotal[7] += dh * uff + d0;
                utotal[8] += (dh * uff + d0) * -2;
                utotal[10] += dh * uff + d0;
                utotal[11] += (dh * uff + d0) * -2;
                if (!train) SetModel("gewu", QPronkPolicy);
            }
        }
        if (Robot == StyleR.legwheeled  && ActionNum == 8)
        {
            
            if (LegwheelTargetMotion == StyleL.walk)
            {
                float[] ktemp = new float[12] { 10, 30, 30, 0, 10, 30, 30, 0, 0, 0, 0, 0 };
                for (int i = 0; i < 12; i++) kb[i] = 2f*ktemp[i];
                for (int i = 0; i < 12; i++) kb2[i] = 0;
                dh = 40;
                T1 = 30;
                ko = 1;
                utotal[1] -= dh * uf1;
                utotal[2] += dh * uf1 * -2;
                utotal[5] += dh * uf2;
                utotal[6] -= dh * uf2 * -2;
                if (!train) SetModel("gewu", LWalkPolicy);
            }
            
            if (LegwheelTargetMotion == StyleL.jump)
            {
                float[] ktemp = new float[12] { 10, 30, 30, 0, 10, 30, 30, 0, 0, 0, 0, 0 };
                for (int i = 0; i < 12; i++) kb[i] = ktemp[i];
                for (int i = 0; i < 12; i++) kb2[i] = 0;
                T2 = 40;
                kh = 5;
                utotal[1] -= 40 * uff;
                utotal[2] -= 80 * uff;
                utotal[5] += 40 * uff;
                utotal[6] += 80 * uff;
                if (!train) SetModel("gewu", LJumpPolicy);
            }
            if (LegwheelTargetMotion == StyleL.drive)
            {
                for (int i = 0; i < 12; i++) kb[i] = 0;
                for (int i = 0; i < 12; i++) kb2[i] = 0;
                kb2[3] = 0.2f;
                kb2[7] = 0.2f;
                //kb1[3] = 1f;
                //kb1[7] = 1f;
                if (!train) SetModel("gewu", LDrivePolicy);
            }
        }

        if (Robot == StyleR.legwheeled && ActionNum == 16)
        {
            if (LegwheelTargetMotion == StyleL.walk)
            {
                T1 = 30;
                dh = 40;
                d0 = 30;
                ko = 1;
                float[] ktemp = new float[16] { 10, 30, 30, 0,    10, 30, 30, 0,    10, 30, 30, 0,    10, 30, 30, 0 };
                for (int i = 0; i < 16; i++) kb[i] = ktemp[i];
                for (int i = 0; i < 16; i++) kb2[i] = 0;
                utotal[1] += dh * uf1 + d0;
                utotal[2] += (dh * uf1 + d0) * -2;
                utotal[5] += dh * uf2 + d0;
                utotal[6] += (dh * uf2 + d0) * -2;
                utotal[9] += dh * uf2 + d0;
                utotal[10] += (dh * uf2 + d0) * -2;
                utotal[13] += dh * uf1 + d0;
                utotal[14] += (dh * uf1 + d0) * -2;
                if (!train) SetModel("gewu", LWalkPolicy);
            }
            
            if (LegwheelTargetMotion == StyleL.jump)
            {
                T2 = 30;
                dh = 30;
                d0 = 30;
                kh = 2;
                float[] ktemp = new float[16] { 10, 30, 50, 0, 10, 30, 50, 0, 10, 30, 50, 0, 10, 30, 50, 0 };
                for (int i = 0; i < 16; i++) kb[i] = ktemp[i];
                for (int i = 0; i < 16; i++) kb2[i] = 0;
                utotal[1] += dh * uff + d0;
                utotal[2] += (dh * uff + d0) * -2;
                utotal[5] += dh * uff + d0;
                utotal[6] += (dh * uff + d0) * -2;
                utotal[9] += dh * uff + d0;
                utotal[10] += (dh * uff + d0) * -2;
                utotal[13] += dh * uff + d0;
                utotal[14] += (dh * uff + d0) * -2;
                if (!train) SetModel("gewu", LJumpPolicy);
            }
            if (LegwheelTargetMotion == StyleL.drive)
            {
                d0 = 30;
                utotal[1] += d0;
                utotal[2] += d0 * -2;
                utotal[5] += d0;
                utotal[6] += d0 * -2;
                utotal[9] += d0;
                utotal[10] += d0 * -2;
                utotal[13] += d0;
                utotal[14] += d0 * -2;
                //kb1[3] = 1f;
                //kb1[7] = 1f;
                for (int i = 0; i < 16; i++) kb[i] = 0;
                for (int i = 0; i < 16; i++) kb2[i] = 0;
                kb2[3] = 01f;
                kb2[7] = 01f;
                kb2[11] = 01f;
                kb2[15] = 01f;
                if (!train) SetModel("gewu", LDrivePolicy);
            }
        }
        if (name.Contains("G1"))
        {
            utotal[1] = Mathf.Clamp(utotal[1], -200f, 0f);
            utotal[7] = Mathf.Clamp(utotal[7], 0f, 200f);
        }
        for (int i = 0; i < ActionNum; i++) SetJointTargetDeg(acts[i], utotal[i]);

        
        if (BipedTargetMotion != LastBmotion || QuadrupedTargetMotion != LastQmotion || LegwheelTargetMotion != LastLmotion) EndEpisode();
        LastBmotion = BipedTargetMotion;
        LastQmotion = QuadrupedTargetMotion;
        LastLmotion = LegwheelTargetMotion;
    }
    void SetJointTargetDeg(ArticulationBody joint, float x)
    {
        var drive = joint.xDrive;
        drive.stiffness = 2000f;//2000f;
        drive.damping = 100f;//100f;
        //drive.forceLimit = 300f;

        drive.target = x;
        joint.xDrive = drive;
    }
    void SetJointTargetPosition(ArticulationBody joint, float x)
    {
        x = (x + 1f) * 0.5f;
        var x1 = Mathf.Lerp(joint.xDrive.lowerLimit, joint.xDrive.upperLimit, x);
        var drive = joint.xDrive;
        drive.stiffness = 2000f;
        drive.damping = 100f;
        drive.forceLimit = 200f;
        drive.target = x1;
        joint.xDrive = drive;
    }
    public override void Heuristic(in ActionBuffers actionsOut)
    {
        
    }

    void FixedUpdate()
    {
        
        if (accelerate) Time.timeScale = 20;
        if (!accelerate) Time.timeScale = 1;


        //Vector3 randomForce=new Vector3(Random.Range(-1f, 1f),0,Random.Range(-1f, 1f));
        //if(Random.Range(0, 100)==1)arts[0].AddForce(10*randomForce, ForceMode.Impulse);
        /*Vector3 Force=new Vector3(50f,0,0);
        if(tt==200)arts[0].AddForce(Force, ForceMode.Impulse);
        Force=new Vector3(0,0,60f);
        if(tt==500)arts[0].AddForce(Force, ForceMode.Impulse);
        Force=new Vector3(-60f,0,0);
        if(tt==800)arts[0].AddForce(Force, ForceMode.Impulse);*/

        tp++;
        tq++;
        tt++;
        if (tp > 0 && tp <= T1)
        {
            tp0 = tp;
            uf1 = (-Mathf.Cos(3.14f * 2 * tp0 / T1) + 1f) / 2f;
            uf2 = 0;
        }
        if (tp > T1 && tp <= 2 * T1)
        {
            tp0 = tp - T1;
            uf1 = 0;
            uf2 = (-Mathf.Cos(3.14f * 2 * tp0 / T1) + 1f) / 2f;
        }
        if (tp >= 2 * T1) tp = 0;
        uff = (-Mathf.Cos(3.14f * 2 * tq / T2) + 1f) / 2f;
        if (tq >= T2) tq = 0;

        var vel = body.InverseTransformDirection(arts[0].velocity);
        var wel = body.InverseTransformDirection(arts[0].angularVelocity);
        var live_reward = 1f;
        var ori_reward1 = -0.1f * Mathf.Abs(EulerTrans(body.eulerAngles[0]));//-0.5f * Mathf.Min(Mathf.Abs(body.eulerAngles[0]), Mathf.Abs(body.eulerAngles[0] - 360f));
        var ori_reward2 = -2f * Mathf.Abs(wel[1]);
        var ori_reward3 = -0.1f * Mathf.Min(Mathf.Abs(body.eulerAngles[2]), Mathf.Abs(body.eulerAngles[2] - 360f));
        var vel_reward1 = vel[2] - Mathf.Abs(vel[0]);
        var vel_reward2 = Mathf.Clamp(vel[2],-2,1.2f) - Mathf.Abs(vel[0]) + kh * Mathf.Abs(vel[1]);
        var reward = live_reward + (ori_reward1 + ori_reward2 + ori_reward3) * ko/4f+ vel_reward2/4f;// + 5*foot.position.y;
        //if (BipedTargetMotion == StyleB.walk) reward += vel_reward1;
        //if (BipedTargetMotion == StyleB.run || BipedTargetMotion == StyleB.jump) reward += vel_reward2;
        AddReward(reward);
        if (Mathf.Abs(EulerTrans(body.eulerAngles[0])) > 40f || Mathf.Abs(EulerTrans(body.eulerAngles[2])) > 40f || tt>=1000)
        {
            EndEpisode();
            //print(stage);
        }
    }

}
