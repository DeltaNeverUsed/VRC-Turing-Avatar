#if UNITY_EDITOR
using System;
using AnimatorAsCode.V0;
using AnimatorAsCodeFramework.Examples;
using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Dynamics.Contact.Components;
using System.Linq;

public class TM : MonoBehaviour
{
    public VRCAvatarDescriptor avatar;
    public AnimatorController assetContainer;
    public string assetKey;
}

[CustomEditor(typeof(TM), true)]
public class TMEditor : Editor
{
    private TM my;
    private AacFlBase aac;

    private int tape_length = 4;
    private int states = 4;
    private GameObject TapePrefab;
    private GameObject StateConfig;

    private GameObject root;

    public override void OnInspectorGUI()
    {
        var prop = serializedObject.FindProperty("assetKey");
        if (prop.stringValue.Trim() == "")
        {
            prop.stringValue = GUID.Generate().ToString();
            serializedObject.ApplyModifiedProperties();
        }

        if (TapePrefab == null) { TapePrefab = (GameObject)AssetDatabase.LoadAssetAtPath("Assets/VRCTMA/Tape.prefab", typeof(GameObject)); } // i don't know of a better way of doing this
        if (StateConfig == null) { StateConfig = (GameObject)AssetDatabase.LoadAssetAtPath("Assets/VRCTMA/StateConfig.prefab", typeof(GameObject)); } // i don't know of a better way of doing this

        DrawDefaultInspector();
        EditorGUILayout.Space(20);

        root = (GameObject)EditorGUILayout.ObjectField("The Object to be attached to:", root, typeof(GameObject), true);
        EditorGUILayout.Space(20);

        tape_length = EditorGUILayout.IntField("Tape Length:", tape_length);
        TapePrefab = (GameObject)EditorGUILayout.ObjectField("Tape prefab:", TapePrefab, typeof(GameObject), false);
        EditorGUILayout.Space(10);

        states = EditorGUILayout.IntField("Number of States:", states);
        StateConfig = (GameObject)EditorGUILayout.ObjectField("StateConfig prefab:", StateConfig, typeof(GameObject), false);
        if (GUILayout.Button("Create"))
        {
            CreateTM();
        }
    }

    // this is never used
    private BlendTree aacBlendTree(AacFlClip zeroClip, AacFlClip oneClip, AacFlFloatParameter manualControlParameter, float minThreshold = 0, float maxThreshold = 1)
    {
        var proxyTree = aac.NewBlendTreeAsRaw();
        proxyTree.blendParameter = manualControlParameter.Name;
        proxyTree.blendType = BlendTreeType.Simple1D;
        proxyTree.minThreshold = minThreshold;
        proxyTree.maxThreshold = maxThreshold;
        proxyTree.useAutomaticThresholds = true;
        proxyTree.children = new[]
        {
                new ChildMotion {motion = zeroClip.Clip, timeScale = 1, threshold = 0},
                new ChildMotion {motion = oneClip.Clip, timeScale = 1, threshold = 1}
        };
        return proxyTree;
    }
    private void CreateTM()
    {
        my = (TM)target;
        aac = AacExample.AnimatorAsCode("TM", my.avatar, my.assetContainer, my.assetKey);

        var fx = aac.CreateMainFxLayer();

        // Logic Paramater stuff

        int move_amount = 2;

        var tape_pos = fx.IntParameter("tape_pos");
        var current_state = fx.IntParameter("current_state");

        var tape_val = fx.BoolParameter("ctape");

        var l_write1 = fx.BoolParameter("l_write1");
        var l_write0 = fx.BoolParameter("l_write0");

        var l_goto1 = fx.IntParameter("l_goto1");
        var l_goto0 = fx.IntParameter("l_goto0");

        var l_move1 = fx.IntParameter("l_move1");
        var l_move0 = fx.IntParameter("l_move0");

        var idle = fx.NewState("Idle");

        var load_state = fx.NewState("Load State").RightOf();

        var logic = fx.NewState("Logic").Shift(load_state, 7, 0);

        // uhh, do stuff i guess. man i don't fuckin' know
        var root_transform = root.transform;

        if (root_transform.Find("SC_holder") != null)
        {
            // lazy bad code
            DestroyImmediate(root_transform.Find("SC_holder").gameObject);
        }

        var SC_holder = new GameObject("SC_holder");
        SC_holder.transform.SetParent(root_transform);

        // create the tape
        AacFlBoolParameter[] tape = new AacFlBoolParameter[tape_length];
        for (int i = 0; i < tape_length; i++) { tape[i] = fx.BoolParameter($"TapeCell{i}"); }

        AacFlBoolParameterGroup[] state_params_b = new AacFlBoolParameterGroup[states]; // w1, w0, w1p, w0p, g1p, g0p
        AacFlIntParameterGroup[] state_params_i = new AacFlIntParameterGroup[states]; // g1, g0
        for (int i = 0; i < states; i++)
        {
            EditorUtility.DisplayProgressBar("Setting up", "Creating StateConfig Animations, this might take a while...", (float)i/(float)states);

            state_params_b[i] = fx.BoolParameters($"S{i}Write1", $"S{i}Write0", $"S{i}Write1Prox", $"S{i}Write0Prox", $"S{i}Goto1Prox", $"S{i}Goto0Prox");
            state_params_i[i] = fx.IntParameters($"S{i}Goto1", $"S{i}Goto0", $"S{i}Move1", $"S{i}Move0");

            var s = Instantiate(StateConfig, new Vector3(0, 0, 0), Quaternion.identity);
            s.name = $"State{i}";
            s.transform.SetParent(SC_holder.transform);

            s.transform.localPosition = new Vector3((i-states/2)*0.75f, 0, 0.6f);
            s.transform.localRotation = Quaternion.Euler(0, 180, 0);

            foreach (Transform child in s.transform)
            {
                if (child.name == "Text")
                {
                    // this thing is gonna have so many materials, but it's fineeeeeee
                    Material m = Instantiate(child.GetComponent<Renderer>().material);
                    AssetDatabase.CreateAsset(m, $"Assets/DMats/{i}.mat");

                    child.GetComponent<Renderer>().material = m;


                    // displaying the stuff
                    for (int l = 0; l < 2; l++)
                    {
                        var state_ui_layer = aac.CreateSupportingFxLayer($"State Config UI M {i} {l}");

                        var eval = state_ui_layer.NewState("Eval");

                        // creating the animations for displaying the state config
                        var left = state_ui_layer.NewState($"left {s.name}")
                            .WithAnimation(aac.NewClip().Animating(clip =>
                            {
                                clip.Animates(child.GetComponent<MeshRenderer>(), $"material._Char{26 + (28 * (1 - l))}").WithOneFrame(44);
                            })).RightOf();
                        var right = state_ui_layer.NewState($"right {s.name}")
                            .WithAnimation(aac.NewClip().Animating(clip =>
                            {
                                clip.Animates(child.GetComponent<MeshRenderer>(), $"material._Char{26 + (28 * (1 - l))}").WithOneFrame(50);
                            })).Under();
                        var halt = state_ui_layer.NewState($"halt {s.name}")
                            .WithAnimation(aac.NewClip().Animating(clip =>
                            {
                                clip.Animates(child.GetComponent<MeshRenderer>(), $"material._Char{26 + (28 * (1 - l))}").WithOneFrame(40);
                            })).Under();


                        eval.TransitionsTo(right).When(state_params_i[i].ToList()[3 - l].IsEqualTo(0));
                        eval.TransitionsTo(left).When(state_params_i[i].ToList()[3 - l].IsEqualTo(1));
                        eval.TransitionsTo(halt).When(state_params_i[i].ToList()[3 - l].IsEqualTo(2));

                        left.Exits().When(state_params_i[i].ToList()[3 - l].IsNotEqualTo(0));
                        right.Exits().When(state_params_i[i].ToList()[3 - l].IsNotEqualTo(1));
                        halt.Exits().When(state_params_i[i].ToList()[3 - l].IsNotEqualTo(2));
                    }
                    for (int l = 0; l < 2; l++)
                    {
                        var state_ui_layer = aac.CreateSupportingFxLayer($"State Config UI {i} {l}");

                        var eval = state_ui_layer.NewState("Eval");

                        // creating the animations for displaying the state config
                        var one = state_ui_layer.NewState($"{s.name} 1")
                            .WithAnimation(aac.NewClip().Animating(clip =>
                            {
                                clip.Animates(child.GetComponent<MeshRenderer>(), $"material._Char{20 + (28 * (1 - l))}").WithOneFrame(17);
                            })).RightOf();
                        var zero = state_ui_layer.NewState($"{s.name} 0")
                            .WithAnimation(aac.NewClip().Animating(clip =>
                            {
                                clip.Animates(child.GetComponent<MeshRenderer>(), $"material._Char{20 + (28 * (1 - l))}").WithOneFrame(16);
                            })).Under();

                        eval.TransitionsTo(one).When(state_params_b[i].ToList()[1 - l].IsTrue());
                        eval.TransitionsTo(zero).When(state_params_b[i].ToList()[1 - l].IsFalse());

                        one.Exits().When(state_params_b[i].ToList()[1 - l].IsFalse());
                        zero.Exits().When(state_params_b[i].ToList()[1 - l].IsTrue());
                    }
                    // creating the animations for displaying what state to goto if the tape is equal to 1 or 0
                    for (int l = 0; l < 2; l++)
                    {
                        var state_ui_layer = aac.CreateSupportingFxLayer($"State Config UI {i} {l} State");
                        var eval = state_ui_layer.NewState("Eval");

                        for (int l2 = 0; l2 < states; l2++) // inconsistant naming but who cares
                        {
                            var c = state_ui_layer.NewState($"{s.name} 1")
                                .WithAnimation(aac.NewClip().Animating(clip =>
                                {
                                    clip.Animates(child.GetComponent<MeshRenderer>(), $"material._Char{33 + (28 * (1 - l))}").WithOneFrame(33 + l2);
                                })).Shift(eval, 1, l2);

                            eval.TransitionsTo(c).When(state_params_i[i].ToList()[1 - l].IsEqualTo(l2));
                            c.Exits().When(state_params_i[i].ToList()[1 - l].IsNotEqualTo(l2));


                        }

                    }
                    var state_ui_layer2 = aac.CreateSupportingFxLayer($"State Config Selection Display {i}"); // i hate this
                    var eval2 = state_ui_layer2.NewState("Eval");

                    var sel_state = state_ui_layer2.NewState($"State {i}").WithAnimation(aac.NewClip().Animating(clip =>
                    {
                        clip.Animates(s.transform, $"m_LocalPosition.x").WithOneFrame(s.transform.localPosition.x);
                        clip.Animates(s.transform, $"m_LocalPosition.y").WithOneFrame(s.transform.localPosition.y);
                        clip.Animates(s.transform, $"m_LocalPosition.z").WithOneFrame(s.transform.localPosition.z + 0.2f);
                    })).RightOf();
                    var not_sel_state = state_ui_layer2.NewState($"State {i}").WithAnimation(aac.NewClip().Animating(clip =>
                    {
                        clip.Animates(s.transform, $"m_LocalPosition.x").WithOneFrame(s.transform.localPosition.x);
                        clip.Animates(s.transform, $"m_LocalPosition.y").WithOneFrame(s.transform.localPosition.y);
                        clip.Animates(s.transform, $"m_LocalPosition.z").WithOneFrame(s.transform.localPosition.z);
                    })).RightOf();

                    eval2.TransitionsTo(sel_state).When(current_state.IsEqualTo(i));
                    sel_state.TransitionsTo(not_sel_state).When(current_state.IsNotEqualTo(i));

                    not_sel_state.Exits().When(current_state.IsNotEqualTo(i));

                    child.GetComponent<MeshRenderer>().sharedMaterial.SetFloat("_Char6", 33+i);

                    continue;
                }

                child.Find("WriteContact").GetComponent<VRCContactReceiver>().parameter = state_params_b[i].ToList()[3 - Convert.ToInt32(child.name)].Name;
                child.Find("GotoContact").GetComponent<VRCContactReceiver>().parameter = state_params_b[i].ToList()[5 - Convert.ToInt32(child.name)].Name;


            }

        }

        // C# is stupid, i can't define these variables(state_ui_layer, and eval) with the same names as what i used in the for loops, yet i can't use them without defining them either. i wanna go back to python
        var state_logic_layer = aac.CreateSupportingFxLayer($"State Config Logic");
        var evall = state_logic_layer.NewState("Eval");

        // Setup the logic for the StateConfigs
        for (int i = 0; i < states; i++)
        {
            EditorUtility.DisplayProgressBar("Setting up state logic", "Creating Animations for state logic", (float)i / (float)states);
            for (int l = 0; l < 2; l++)
            {
                // Reset goto state if it goes above the amount of states we have
                var reset = state_logic_layer.NewState($"Reset State {i} {l}").Drives(state_params_i[i].ToList()[1 - l], 0).Shift(evall, -1, i * 2 + l);
                evall.TransitionsTo(reset).When(state_params_i[i].ToList()[1 - l].IsGreaterThan(states));
                reset.AutomaticallyMovesTo(evall);

                // Reset move
                reset = state_logic_layer.NewState($"Reset Move {i} {l}").Drives(state_params_i[i].ToList()[3 - l], 0).Shift(evall, -2, i * 2 + l);
                evall.TransitionsTo(reset).When(state_params_i[i].ToList()[3 - l].IsGreaterThan(move_amount));
                reset.AutomaticallyMovesTo(evall);

                // Toggles between 1 and 0 on the writes
                var change_write = state_logic_layer.NewState($"Change Write {i} {l}").Drives(state_params_b[i].ToList()[1 - l], true).Shift(evall, 1, i * 2 + l + 1);
                evall.TransitionsTo(change_write).When(state_params_b[i].ToList()[3 - l].IsTrue())
                    .And(state_params_b[i].ToList()[1 - l].IsFalse());
                change_write.AutomaticallyMovesTo(evall);

                change_write = state_logic_layer.NewState($"Change Write {i} {l}").Drives(state_params_b[i].ToList()[1 - l], false).Shift(evall, 2, i * 2 + l + 1);
                evall.TransitionsTo(change_write).When(state_params_b[i].ToList()[3 - l].IsTrue())
                    .And(state_params_b[i].ToList()[1 - l].IsTrue());
                change_write.AutomaticallyMovesTo(evall);

                // increase goto thing
                var increase_goto = state_logic_layer.NewState($"increase Goto {i} {l}").DrivingIncreases(state_params_i[i].ToList()[1 - l], 1).Shift(evall, 0, i * 2 + l + 1);
                evall.TransitionsTo(increase_goto).When(state_params_b[i].ToList()[5 - l].IsTrue());
                increase_goto.AutomaticallyMovesTo(evall);
            }
        }

        // load current state into local state
        // i should move this somewhere else, and rewrite the code above to use this
        // NOTE: variables could have been named better
        // param is the paramater where the value get's copied to
        // target_param is the paramater that get's copied from
        void CopyBool(AacFlBoolParameter param, AacFlBoolParameter target_param, AacFlState start, AacFlState exit, int shift_amount, IAacFlCondition condition)
        {
            var one = fx.NewState($"{param.Name} 0").Drives(param, false).Shift(start, 0, 1 + shift_amount * 2);
            var zero = fx.NewState($"{param.Name} 1").Drives(param, true).Under();
            start.TransitionsTo(one).When(target_param.IsFalse()).And(condition);
            start.TransitionsTo(zero).When(target_param.IsTrue()).And(condition);

            one.AutomaticallyMovesTo(exit);
            zero.AutomaticallyMovesTo(exit);
        }
        // i should redo these functions to be more flexable instead of having multiple. yet again another reason to start over 
        void CopyBoolAndInvert(AacFlLayer layer,AacFlBoolParameter param, AacFlBoolParameter target_param, AacFlState start, AacFlState exit, int shift_amount, IAacFlCondition condition, IAacFlCondition condition2)
        {
            var one = layer.NewState($"{param.Name} 0").Drives(param, true).Shift(start, 0, 1 + shift_amount * 2);
            var zero = layer.NewState($"{param.Name} 1").Drives(param, false).Under();
            start.TransitionsTo(one).When(target_param.IsFalse()).And(condition).And(condition2);
            start.TransitionsTo(zero).When(target_param.IsTrue()).And(condition).And(condition2);

            one.AutomaticallyMovesTo(exit);
            zero.AutomaticallyMovesTo(exit);
        }

        // this is stupid, i should just rewrite the stuff above to use Floats
        void CopyInt(AacFlIntParameter param, AacFlIntParameter target_param, AacFlState start, AacFlState exit, int man_num, int shift_amount, IAacFlCondition condition) 
        {
            for (int i = 0; i < man_num; i++)
            {
                var num = fx.NewState($"{param.Name} 0")
                    .WithAnimation(aac.NewClip().Animating(clip =>
                    {
                        clip.Animates(my.avatar.GetComponent<Animator>(), param.Name).WithOneFrame(i);
                    })).Drives(param, i).Shift(start, 0,1+i+shift_amount*man_num);
                start.TransitionsTo(num).When(target_param.IsEqualTo(i)).And(condition);
                num.AutomaticallyMovesTo(exit);
            }
        }


        // this is so scuffed
        // this is for copying the state config from whatever state your into the "local" state
        var temp_bool0 = fx.NewState("Copy Bool0").Shift(load_state,1,0);
        var temp_bool1 = fx.NewState("Copy Bool1").RightOf();

        var temp_int0 = fx.NewState("Copy Int0").RightOf();
        var temp_int1 = fx.NewState("Copy Int1").RightOf();

        var temp_int2 = fx.NewState("Copy Int2").RightOf();
        var temp_int3 = fx.NewState("Copy Int3").RightOf();

        for (int i = 0; i < tape_length; i++)
        {
            EditorUtility.DisplayProgressBar("Setting up logic", "Creating Animations for copying tape value", (float)i / (float)tape_length);
            CopyBool(tape_val, tape[i], load_state, temp_bool0, i, tape_pos.IsEqualTo(i));
        }

        for (int state = 0; state < states; state++)
        {
            EditorUtility.DisplayProgressBar("Setting up logic", "Creating Animations for copying state value, this might take a while...", (float)state / (float)states);
            // copying
            CopyBool(l_write1, state_params_b[state].ToList()[0], temp_bool0, temp_bool1, state, current_state.IsEqualTo(state));
            CopyBool(l_write0, state_params_b[state].ToList()[1], temp_bool1, temp_int0, state, current_state.IsEqualTo(state));

            CopyInt(l_goto1, state_params_i[state].ToList()[0], temp_int0, temp_int1, states, state, current_state.IsEqualTo(state));
            CopyInt(l_goto0, state_params_i[state].ToList()[1], temp_int1, temp_int2, states, state, current_state.IsEqualTo(state));

            CopyInt(l_move1, state_params_i[state].ToList()[2], temp_int2, temp_int3, move_amount+1, state, current_state.IsEqualTo(state));
            CopyInt(l_move0, state_params_i[state].ToList()[3], temp_int3, logic, move_amount+1, state, current_state.IsEqualTo(state));

        }

        // the actual logic, sorry for the lack of comments
        var end = fx.NewState($"Exit").Shift(logic, 10, -5);
        end.Exits().When(fx.BoolParameter("DoesNothing").IsEqualTo(false));



        for (int t_val = 0; t_val < 2; t_val++)
        {
            EditorUtility.DisplayProgressBar("Setting up logic", "Creating Animations for the logic", (float)t_val / 2f);
            var tape_state = fx.NewState($"Tape State {t_val}").Shift(logic,1,t_val);
            logic.TransitionsTo(tape_state).When(tape_val.IsEqualTo(Convert.ToBoolean(t_val)));

            var write_state = fx.NewState($"Tape State {t_val}").Shift(tape_state, 1, t_val * (tape_length*2));
            var write_state_exit = fx.NewState($"Tape State {t_val} Exit").RightOf();

            tape_state.AutomaticallyMovesTo(write_state);

            for (int i = 0; i < tape_length; i++)
            {
                CopyBool(tape[i], t_val == 0 ? l_write0 : l_write1, write_state, write_state_exit, i, tape_pos.IsEqualTo(i));
            }

            var change_c_state = fx.NewState($"Change Current State {t_val}").Shift(write_state_exit, 1, 0);
            var change_c_state_exit = fx.NewState($"Change Current State {t_val} Exit").RightOf();

            write_state_exit.AutomaticallyMovesTo(change_c_state);

            CopyInt(current_state, t_val == 0 ? l_goto0 : l_goto1, change_c_state, change_c_state_exit, states, 0, fx.BoolParameter("DoesNothing").IsEqualTo(false));

            var move_l = fx.NewState($"Move Tape {t_val} Left")
                .DrivingDecreases(tape_pos, 1)
                .AutomaticallyMovesTo(end)
                .Shift(change_c_state_exit, 1, 0);
            var move_r = fx.NewState($"Move Tape {t_val} Right")
                .DrivingIncreases(tape_pos, 1)
                .AutomaticallyMovesTo(end)
                .Shift(change_c_state_exit, 1, 1);
            var move_h = fx.NewState($"Move Tape {t_val} Halt")
                .AutomaticallyMovesTo(end)
                .Shift(change_c_state_exit, 1, 2);

            change_c_state_exit.TransitionsTo(move_r).When((t_val == 0 ? l_move0 : l_move1).IsEqualTo(0));
            change_c_state_exit.TransitionsTo(move_l).When((t_val == 0 ? l_move0 : l_move1).IsEqualTo(1));
            change_c_state_exit.TransitionsTo(move_h).When((t_val == 0 ? l_move0 : l_move1).IsEqualTo(2));

        }
        

        idle.TransitionsTo(load_state).When(fx.BoolParameter("Enable").IsTrue());

        
        // Tape ui suff, this should really be above, but i'm lazy
        var state_tapeUI_layer = aac.CreateSupportingFxLayer($"Tape Move Layer");
        var tapeUI_eval = state_tapeUI_layer.NewState("Eval");

        if (root_transform.Find("Tape") != null) { DestroyImmediate(root_transform.Find("Tape").gameObject); }
        var t = Instantiate(TapePrefab, new Vector3(0, 0, 0), Quaternion.identity);
        t.name = $"Tape";
        t.transform.SetParent(root_transform);

        t.transform.localPosition = new Vector3(0, 0, 1.9f);
        var tape_display = t.transform.Find("Display");
        tape_display.transform.localScale = new Vector3(1,0.5f,1);

        for (int i = 0; i < tape_length; i++)
        {
            EditorUtility.DisplayProgressBar("Setting up logic", "Creating Animations moving the tape", (float)i / (float)tape_length);
            // moving the tape
            var move = state_tapeUI_layer.NewState($"MoveTape")
                .WithAnimation(aac.NewClip().Animating(clip =>
                {
                    clip.Animates(tape_display.transform, $"m_LocalPosition.x").WithOneFrame(16 - (float)i / 4f);
                })).RightOf();
            tapeUI_eval.TransitionsTo(move).When(tape_pos.IsEqualTo(i));
            move.Exits().When(fx.BoolParameter("DoesNothing").IsEqualTo(false));

            // showing the values on the tape
            var tape_ui_layer = aac.CreateSupportingFxLayer($"Tape Num Display {i}");

            var eval = tape_ui_layer.NewState("Eval");

            // creating the animations for displaying the state config
            var one = tape_ui_layer.NewState($"1")
                .WithAnimation(aac.NewClip().Animating(clip =>
                {
                    clip.Animates(tape_display.GetComponent<MeshRenderer>(), $"material._Char{i}").WithOneFrame(17);
                    clip.Animates(tape_display.transform, $"m_LocalScale.x").WithOneFrame(32); // scuffed thing to keep the bounding box on the upload down
                })).RightOf();
            var zero = tape_ui_layer.NewState($"0")
                .WithAnimation(aac.NewClip().Animating(clip =>
                {
                    clip.Animates(tape_display.GetComponent<MeshRenderer>(), $"material._Char{i}").WithOneFrame(16);
                    clip.Animates(tape_display.transform, $"m_LocalScale.x").WithOneFrame(32); // scuffed thing to keep the bounding box on the upload down
                })).Under();


            eval.TransitionsTo(one).When(tape[i].IsTrue());
            eval.TransitionsTo(zero).When(tape[i].IsFalse());

            one.Exits().When(tape[i].IsFalse());
            zero.Exits().When(tape[i].IsTrue());

        }

        // yet again, i should rewrite the stuff above.
        // at this point this whole thing is such a mess i should just start from scratch pretty much
        void toggle_button(string param_name, Transform Object, int num)
        {
            var param = fx.BoolParameter(param_name);
            var state = state_logic_layer.NewState(param_name).DrivingIncreases(tape_pos, num).RightOf();
            Object.GetComponent<VRCContactReceiver>().parameter = param_name;
            evall.TransitionsTo(state).When(param.IsTrue());
            state.AutomaticallyMovesTo(evall);
        }

        toggle_button("MTLP", t.transform.Find("MoveLeft"), -1);
        toggle_button("MTRP", t.transform.Find("MoveRight"), 1);

        // scuffed
        var TTB = fx.BoolParameter("TTB");
        t.transform.Find("BitToggle").GetComponent<VRCContactReceiver>().parameter = "TTB";
        for (int i = 0; i < tape_length; i++)
        {
            EditorUtility.DisplayProgressBar("Setting up logic", "Creating Animations for a player changing the tape", (float)i / (float)tape_length);
            CopyBoolAndInvert(state_logic_layer, tape[i], tape[i], evall, evall, i, tape_pos.IsEqualTo(i), TTB.IsTrue());
        }

        EditorUtility.ClearProgressBar();
    }
}

#endif